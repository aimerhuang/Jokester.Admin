using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

internal sealed class PointBucketLedger(ISqlSugarClient db)
{
    internal const int PackageSpendPriority = 0;
    internal const int SignInSpendPriority = 100;
    internal const int PermanentPackageSpendPriority = 200;
    private const int LegacySignInGiftPoints = 25;

    public async Task GrantAsync(
        long userId,
        int points,
        string source,
        string businessKey,
        DateTime? expiresAt,
        int spendPriority,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (userId <= 0
            || points <= 0
            || (expiresAt.HasValue && expiresAt.Value <= now)
            || string.IsNullOrWhiteSpace(businessKey))
        {
            throw new AppException(ErrorCodes.ServerError, "积分批次数据无效");
        }

        await db.Insertable(new PointBucketEntity
        {
            UserId = userId,
            Source = source,
            BusinessKey = businessKey,
            GrantedPoints = points,
            RemainingPoints = points,
            ExpiredPoints = 0,
            ExpiresAt = expiresAt,
            SpendPriority = spendPriority,
            CreatedAt = now
        }).ExecuteCommandAsync(cancellationToken);
    }

    public async Task<int> ExpireAsync(
        SysUserEntity user,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var expiredBuckets = await db.Queryable<PointBucketEntity>()
            .TranLock(DbLockType.Wait)
            .Where(x => x.UserId == user.Id
                && x.RemainingPoints > 0
                && x.ExpiresAt != null
                && x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        if (expiredBuckets.Count == 0)
        {
            return 0;
        }

        var totalExpired = expiredBuckets.Aggregate(
            0,
            (total, bucket) => checked(total + bucket.RemainingPoints));
        if (totalExpired > user.PointBalance)
        {
            throw new AppException(ErrorCodes.ServerError, "积分批次余额与用户总余额不一致");
        }

        var balanceBefore = user.PointBalance;
        var runningBalance = balanceBefore;
        foreach (var bucket in expiredBuckets)
        {
            var expiredPoints = bucket.RemainingPoints;
            var bucketUpdated = await db.Updateable<PointBucketEntity>()
                .SetColumns(x => new PointBucketEntity
                {
                    RemainingPoints = 0,
                    ExpiredPoints = checked(bucket.ExpiredPoints + expiredPoints),
                    UpdatedAt = now
                })
                .Where(x => x.Id == bucket.Id && x.RemainingPoints == expiredPoints)
                .ExecuteCommandAsync(cancellationToken);
            if (bucketUpdated != 1)
            {
                throw new AppException(ErrorCodes.ServerError, "限时积分过期状态发生变化，请重试");
            }

            runningBalance -= expiredPoints;
            // A late task refund can restore an already-expired bucket, so each
            // real expiration settlement needs its own ledger business key.
            await db.Insertable(new UserPointDetailEntity
            {
                UserId = user.Id,
                ChangePoints = -expiredPoints,
                BalanceAfter = runningBalance,
                ChangeType = "expire",
                Source = GetExpirationSource(bucket.Source),
                BusinessKey = $"point-bucket:{bucket.Id}:expire:{Guid.NewGuid():N}",
                Remark = $"限时积分到期，批次ID：{bucket.Id}",
                CreatedAt = now
            }).ExecuteCommandAsync(cancellationToken);
        }

        var userUpdated = await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity
            {
                PointBalance = runningBalance,
                UpdatedAt = now
            })
            .Where(x => x.Id == user.Id && !x.IsDeleted && x.PointBalance == balanceBefore)
            .ExecuteCommandAsync(cancellationToken);
        if (userUpdated != 1)
        {
            throw new AppException(ErrorCodes.ServerError, "限时积分过期处理失败，请重试");
        }

        user.PointBalance = runningBalance;
        return totalExpired;
    }

    public async Task ExpireLegacyPreviousSignInPointsAsync(
        SysUserEntity user,
        DateTime todayStart,
        CancellationToken cancellationToken)
    {
        var signedInToday = await db.Queryable<UserPointDetailEntity>()
            .AnyAsync(x => x.UserId == user.Id
                && x.Source == "sign_in"
                && x.ChangePoints > 0
                && x.CreatedAt >= todayStart
                && x.CreatedAt < todayStart.AddDays(1),
                cancellationToken);
        if (signedInToday)
        {
            return;
        }

        var lastSignIn = await db.Queryable<UserPointDetailEntity>()
            .Where(x => x.UserId == user.Id
                && x.Source == "sign_in"
                && x.ChangePoints > 0
                && x.CreatedAt < todayStart)
            .OrderBy(x => x.CreatedAt, OrderByType.Desc)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        if (lastSignIn is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(lastSignIn.BusinessKey))
        {
            var hasBucket = await db.Queryable<PointBucketEntity>()
                .AnyAsync(x => x.UserId == user.Id && x.BusinessKey == lastSignIn.BusinessKey, cancellationToken);
            if (hasBucket)
            {
                return;
            }
        }

        var alreadyExpired = await db.Queryable<UserPointDetailEntity>()
            .AnyAsync(x => x.UserId == user.Id
                && x.Source == "sign_in_expire"
                && x.CreatedAt >= todayStart,
                cancellationToken);
        if (alreadyExpired)
        {
            return;
        }

        var consumedAfterLastSignIn = await db.Queryable<UserPointDetailEntity>()
            .Where(x => x.UserId == user.Id
                && x.ChangePoints < 0
                && x.CreatedAt >= lastSignIn.CreatedAt
                && x.CreatedAt < todayStart)
            .SumAsync(x => -x.ChangePoints);
        var remainingSignInPoints = Math.Max(0, LegacySignInGiftPoints - consumedAfterLastSignIn);
        var expirePoints = Math.Min(remainingSignInPoints, user.PointBalance);
        if (expirePoints <= 0)
        {
            return;
        }

        var balanceBefore = user.PointBalance;
        var balanceAfter = balanceBefore - expirePoints;
        var affected = await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity
            {
                PointBalance = balanceAfter,
                UpdatedAt = DateTime.Now
            })
            .Where(x => x.Id == user.Id && !x.IsDeleted && x.PointBalance == balanceBefore)
            .ExecuteCommandAsync(cancellationToken);
        if (affected != 1)
        {
            throw new AppException(ErrorCodes.ServerError, "积分过期处理失败，请重试");
        }

        user.PointBalance = balanceAfter;
        await db.Insertable(new UserPointDetailEntity
        {
            UserId = user.Id,
            ChangePoints = -expirePoints,
            BalanceAfter = balanceAfter,
            ChangeType = "expire",
            Source = "sign_in_expire",
            BusinessKey = $"sign-in-expire:{user.Id}:{todayStart:yyyyMMdd}",
            Remark = "清除上一日未使用的签到积分",
            CreatedAt = DateTime.Now
        }).ExecuteCommandAsync(cancellationToken);
    }

    public async Task AllocateAsync(
        SysUserEntity user,
        IReadOnlyList<PointConsumption> consumptions,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (consumptions.Count == 0)
        {
            return;
        }
        if (consumptions.Any(x => x.Points <= 0 || string.IsNullOrWhiteSpace(x.BusinessKey)))
        {
            throw new AppException(ErrorCodes.ServerError, "积分扣款分摊数据无效");
        }

        var buckets = await db.Queryable<PointBucketEntity>()
            .TranLock(DbLockType.Wait)
            .Where(x => x.UserId == user.Id
                && x.RemainingPoints > 0
                && (x.ExpiresAt == null || x.ExpiresAt > now))
            .OrderBy(x => x.SpendPriority)
            .OrderBy(x => x.ExpiresAt)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var activeBucketPoints = buckets.Aggregate(
            0,
            (total, bucket) => checked(total + bucket.RemainingPoints));
        if (activeBucketPoints > user.PointBalance)
        {
            throw new AppException(ErrorCodes.ServerError, "积分批次余额与用户总余额不一致");
        }

        var originalRemaining = buckets.ToDictionary(x => x.Id, x => x.RemainingPoints);
        var usages = new List<PointBucketUsageEntity>();
        foreach (var consumption in consumptions)
        {
            var unallocated = consumption.Points;
            foreach (var bucket in buckets)
            {
                if (unallocated == 0)
                {
                    break;
                }
                if (bucket.RemainingPoints == 0)
                {
                    continue;
                }

                var usedPoints = Math.Min(bucket.RemainingPoints, unallocated);
                bucket.RemainingPoints -= usedPoints;
                unallocated -= usedPoints;
                usages.Add(new PointBucketUsageEntity
                {
                    BucketId = bucket.Id,
                    UserId = user.Id,
                    BusinessKey = consumption.BusinessKey,
                    UsedPoints = usedPoints,
                    RefundedPoints = 0,
                    CreatedAt = now
                });
            }
        }

        foreach (var bucket in buckets.Where(x => x.RemainingPoints != originalRemaining[x.Id]))
        {
            var bucketUpdated = await db.Updateable<PointBucketEntity>()
                .SetColumns(x => new PointBucketEntity
                {
                    RemainingPoints = bucket.RemainingPoints,
                    UpdatedAt = now
                })
                .Where(x => x.Id == bucket.Id && x.RemainingPoints == originalRemaining[bucket.Id])
                .ExecuteCommandAsync(cancellationToken);
            if (bucketUpdated != 1)
            {
                throw new AppException(ErrorCodes.Conflict, "积分批次余额发生变化，请重试");
            }
        }

        if (usages.Count > 0)
        {
            await db.Insertable(usages).ExecuteCommandAsync(cancellationToken);
        }
    }

    public async Task<PointConsumptionSettlement> SettleConsumptionAsync(
        long userId,
        string businessKey,
        int totalConsumedPoints,
        int refundPoints,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (totalConsumedPoints <= 0 || refundPoints < 0 || refundPoints > totalConsumedPoints)
        {
            throw new AppException(ErrorCodes.ServerError, "积分退款分摊数据无效");
        }

        var usages = await db.Queryable<PointBucketUsageEntity>()
            .TranLock(DbLockType.Wait)
            .Where(x => x.UserId == userId
                && x.BusinessKey == businessKey)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        if (usages.Count == 0)
        {
            return new PointConsumptionSettlement(refundPoints, []);
        }

        var bucketConsumedPoints = usages.Sum(x => x.UsedPoints);
        if (totalConsumedPoints < bucketConsumedPoints
            || usages.Any(x => x.RefundedPoints < 0
                || x.DeferredClawbackPoints < 0
                || x.RefundedPoints + x.DeferredClawbackPoints > x.UsedPoints
                || (x.DeferredClawbackPoints > 0 && string.IsNullOrWhiteSpace(x.DeferredClawbackBusinessKey))))
        {
            throw new AppException(ErrorCodes.ServerError, "积分退款分摊数据不一致");
        }

        var permanentConsumedPoints = totalConsumedPoints - bucketConsumedPoints;
        var permanentRefundPoints = Math.Min(permanentConsumedPoints, refundPoints);
        var remainingRefund = refundPoints - permanentRefundPoints;

        var bucketIds = usages.Select(x => x.BucketId).Distinct().ToArray();
        var buckets = await db.Queryable<PointBucketEntity>()
            .TranLock(DbLockType.Wait)
            .Where(x => x.UserId == userId && bucketIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var bucketLookup = buckets.ToDictionary(x => x.Id);
        var creditedPoints = permanentRefundPoints;
        var suppressedRefunds = new Dictionary<long, int>();
        foreach (var usage in usages)
        {
            if (remainingRefund == 0)
            {
                break;
            }
            if (!bucketLookup.TryGetValue(usage.BucketId, out var bucket))
            {
                throw new AppException(ErrorCodes.ServerError, "积分退款批次不存在");
            }

            var unsettledPoints = usage.UsedPoints - usage.RefundedPoints;
            if (unsettledPoints <= 0)
            {
                continue;
            }
            var nominalRefund = Math.Min(unsettledPoints, remainingRefund);
            var suppressedRefund = Math.Min(usage.DeferredClawbackPoints, nominalRefund);
            var restored = nominalRefund - suppressedRefund;
            suppressedRefunds[usage.Id] = suppressedRefund;
            remainingRefund -= nominalRefund;
            if (restored == 0)
            {
                continue;
            }

            var bucketRemainingBefore = bucket.RemainingPoints;
            var usageRefundedBefore = usage.RefundedPoints;
            bucket.RemainingPoints = checked(bucket.RemainingPoints + restored);
            usage.RefundedPoints += restored;

            var bucketUpdated = await db.Updateable<PointBucketEntity>()
                .SetColumns(x => new PointBucketEntity
                {
                    RemainingPoints = bucket.RemainingPoints,
                    UpdatedAt = now
                })
                .Where(x => x.Id == bucket.Id && x.RemainingPoints == bucketRemainingBefore)
                .ExecuteCommandAsync(cancellationToken);
            var usageUpdated = await db.Updateable<PointBucketUsageEntity>()
                .SetColumns(x => new PointBucketUsageEntity
                {
                    RefundedPoints = usage.RefundedPoints,
                    UpdatedAt = now
                })
                .Where(x => x.Id == usage.Id && x.RefundedPoints == usageRefundedBefore)
                .ExecuteCommandAsync(cancellationToken);
            if (bucketUpdated != 1 || usageUpdated != 1)
            {
                throw new AppException(ErrorCodes.Conflict, "积分退款分摊状态发生变化，请重试");
            }

            creditedPoints += restored;
        }

        if (remainingRefund != 0)
        {
            throw new AppException(ErrorCodes.ServerError, "积分退款金额无法映射到原扣款");
        }

        var deferredClawbacks = usages
            .Where(x => x.DeferredClawbackPoints > 0)
            .GroupBy(x => x.DeferredClawbackBusinessKey!, StringComparer.Ordinal)
            .Select(group => new DeferredPointClawback(
                group.Key,
                group.Sum(x => x.DeferredClawbackPoints - suppressedRefunds.GetValueOrDefault(x.Id))))
            .Where(x => x.Points > 0)
            .ToArray();
        return new PointConsumptionSettlement(creditedPoints, deferredClawbacks);
    }

    public async Task<int> DeferPendingGrantClawbacksAsync(
        long userId,
        string grantBusinessKey,
        string clawbackBusinessKey,
        int maxPoints,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (maxPoints <= 0)
        {
            return 0;
        }

        var bucket = await db.Queryable<PointBucketEntity>()
            .TranLock(DbLockType.Wait)
            .FirstAsync(x => x.UserId == userId && x.BusinessKey == grantBusinessKey, cancellationToken);
        if (bucket is null)
        {
            return 0;
        }

        // The user row is locked by the caller. A settlement that began first will
        // commit before this query; one that began later cannot pass that user lock.
        var pendingTaskIds = await db.Queryable<AiImageTaskEntity>()
            .Where(x => x.UserId == userId
                && !x.IsDeleted
                && x.BillingStatus == 0
                && (x.Status == 0 || x.Status == 3))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (pendingTaskIds.Count == 0)
        {
            return 0;
        }

        var pendingBusinessKeys = pendingTaskIds
            .Select(id => $"image:{id}:reserve")
            .ToArray();
        var usages = await db.Queryable<PointBucketUsageEntity>()
            .TranLock(DbLockType.Wait)
            .Where(x => x.UserId == userId
                && x.BucketId == bucket.Id
                && pendingBusinessKeys.Contains(x.BusinessKey))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var deferredPoints = 0;
        foreach (var usage in usages)
        {
            if (deferredPoints == maxPoints)
            {
                break;
            }
            if (usage.DeferredClawbackPoints > 0
                && !string.Equals(usage.DeferredClawbackBusinessKey, clawbackBusinessKey, StringComparison.Ordinal))
            {
                throw new AppException(ErrorCodes.ServerError, "生图预留积分已关联其他退款追扣");
            }

            var available = usage.UsedPoints - usage.RefundedPoints - usage.DeferredClawbackPoints;
            if (available <= 0)
            {
                continue;
            }

            var deferred = Math.Min(available, maxPoints - deferredPoints);
            var deferredBefore = usage.DeferredClawbackPoints;
            usage.DeferredClawbackPoints += deferred;
            usage.DeferredClawbackBusinessKey = clawbackBusinessKey;
            var usageUpdated = await db.Updateable<PointBucketUsageEntity>()
                .SetColumns(x => new PointBucketUsageEntity
                {
                    DeferredClawbackPoints = usage.DeferredClawbackPoints,
                    DeferredClawbackBusinessKey = clawbackBusinessKey,
                    UpdatedAt = now
                })
                .Where(x => x.Id == usage.Id && x.DeferredClawbackPoints == deferredBefore)
                .ExecuteCommandAsync(cancellationToken);
            if (usageUpdated != 1)
            {
                throw new AppException(ErrorCodes.Conflict, "生图预留积分退款状态发生变化，请重试");
            }

            deferredPoints += deferred;
        }

        return deferredPoints;
    }

    public async Task<int> GetRevocableGrantPointsAsync(
        long userId,
        string grantBusinessKey,
        int grantedPoints,
        CancellationToken cancellationToken)
    {
        var bucket = await db.Queryable<PointBucketEntity>()
            .TranLock(DbLockType.Wait)
            .FirstAsync(x => x.UserId == userId && x.BusinessKey == grantBusinessKey, cancellationToken);
        if (bucket is null)
        {
            return grantedPoints;
        }
        if (bucket.ExpiredPoints < 0 || bucket.ExpiredPoints > grantedPoints)
        {
            throw new AppException(ErrorCodes.ServerError, "Apple 积分批次过期数据不一致");
        }

        return grantedPoints - bucket.ExpiredPoints;
    }

    public async Task<int> AllocateClawbackAsync(
        SysUserEntity user,
        string preferredGrantBusinessKey,
        string clawbackBusinessKey,
        int points,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (points <= 0)
        {
            return 0;
        }
        if (points > user.PointBalance)
        {
            throw new AppException(ErrorCodes.ServerError, "积分追扣金额超过当前余额");
        }

        var preferredGrant = await db.Queryable<PointBucketEntity>()
            .TranLock(DbLockType.Wait)
            .FirstAsync(x => x.UserId == user.Id && x.BusinessKey == preferredGrantBusinessKey, cancellationToken);
        var buckets = await db.Queryable<PointBucketEntity>()
            .TranLock(DbLockType.Wait)
            .Where(x => x.UserId == user.Id
                && x.RemainingPoints > 0
                && (x.ExpiresAt == null || x.ExpiresAt > now))
            .ToListAsync(cancellationToken);
        var activeBucketPoints = buckets.Sum(x => x.RemainingPoints);
        if (activeBucketPoints > user.PointBalance)
        {
            throw new AppException(ErrorCodes.ServerError, "积分批次余额与用户总余额不一致");
        }

        var originalRemaining = buckets.ToDictionary(x => x.Id, x => x.RemainingPoints);
        var usages = new List<PointBucketUsageEntity>();
        var unallocated = points;

        void UseBucket(PointBucketEntity bucket)
        {
            if (unallocated == 0 || bucket.RemainingPoints == 0)
            {
                return;
            }

            var usedPoints = Math.Min(bucket.RemainingPoints, unallocated);
            bucket.RemainingPoints -= usedPoints;
            unallocated -= usedPoints;
            usages.Add(new PointBucketUsageEntity
            {
                BucketId = bucket.Id,
                UserId = user.Id,
                BusinessKey = clawbackBusinessKey,
                UsedPoints = usedPoints,
                RefundedPoints = 0,
                CreatedAt = now
            });
        }

        var preferredBucket = preferredGrant is null
            ? null
            : buckets.FirstOrDefault(x => x.Id == preferredGrant.Id);
        if (preferredBucket is not null)
        {
            UseBucket(preferredBucket);
        }

        var permanentPoints = user.PointBalance - activeBucketPoints;
        unallocated -= Math.Min(permanentPoints, unallocated);

        if (preferredGrant is not null)
        {
            foreach (var bucket in buckets
                         .Where(x => preferredBucket is null || x.Id != preferredBucket.Id)
                         .OrderByDescending(x => x.SpendPriority)
                         .ThenByDescending(x => x.ExpiresAt)
                         .ThenByDescending(x => x.Id))
            {
                UseBucket(bucket);
            }
        }
        if (preferredGrant is not null && unallocated != 0)
        {
            throw new AppException(ErrorCodes.ServerError, "积分追扣分摊失败");
        }

        foreach (var bucket in buckets.Where(x => x.RemainingPoints != originalRemaining[x.Id]))
        {
            var bucketUpdated = await db.Updateable<PointBucketEntity>()
                .SetColumns(x => new PointBucketEntity
                {
                    RemainingPoints = bucket.RemainingPoints,
                    UpdatedAt = now
                })
                .Where(x => x.Id == bucket.Id && x.RemainingPoints == originalRemaining[bucket.Id])
                .ExecuteCommandAsync(cancellationToken);
            if (bucketUpdated != 1)
            {
                throw new AppException(ErrorCodes.Conflict, "积分批次余额发生变化，请重试");
            }
        }

        if (usages.Count > 0)
        {
            await db.Insertable(usages).ExecuteCommandAsync(cancellationToken);
        }

        return points - unallocated;
    }

    public async Task ApplyDeferredClawbacksAsync(
        SysUserEntity user,
        long taskId,
        IReadOnlyList<DeferredPointClawback> clawbacks,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var clawback in clawbacks)
        {
            var transactionId = ParseAppleRefundBusinessKey(clawback.BusinessKey);
            var detailBusinessKey = $"apple:{transactionId}:task:{taskId}";
            var requestedDeduction = Math.Min(user.PointBalance, clawback.Points);
            var deducted = requestedDeduction > 0
                ? await AllocateClawbackAsync(
                    user,
                    $"apple:{transactionId}:fulfill",
                    detailBusinessKey,
                    requestedDeduction,
                    now,
                    cancellationToken)
                : 0;
            if (deducted > 0)
            {
                var balanceBefore = user.PointBalance;
                var balanceAfter = balanceBefore - deducted;
                var userUpdated = await db.Updateable<SysUserEntity>()
                    .SetColumns(x => new SysUserEntity
                    {
                        PointBalance = balanceAfter,
                        UpdatedAt = now
                    })
                    .Where(x => x.Id == user.Id && !x.IsDeleted && x.PointBalance == balanceBefore)
                    .ExecuteCommandAsync(cancellationToken);
                if (userUpdated != 1)
                {
                    throw new AppException(ErrorCodes.Conflict, "Apple 延后积分追扣时余额发生变化，请重试");
                }

                user.PointBalance = balanceAfter;
                await db.Insertable(new UserPointDetailEntity
                {
                    UserId = user.Id,
                    ChangePoints = -deducted,
                    BalanceAfter = balanceAfter,
                    ChangeType = "refund",
                    Source = "apple_refund",
                    BusinessKey = detailBusinessKey,
                    Remark = $"Apple 退款关联生图任务成功，延后追扣积分，任务ID：{taskId}",
                    CreatedAt = now
                }).ExecuteCommandAsync(cancellationToken);
            }

            var debtPoints = clawback.Points - deducted;
            if (debtPoints <= 0)
            {
                continue;
            }

            var debt = await db.Queryable<AppleIapDebtEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.UserId == user.Id && x.TransactionId == transactionId, cancellationToken);
            if (debt is null)
            {
                await db.Insertable(new AppleIapDebtEntity
                {
                    UserId = user.Id,
                    TransactionId = transactionId,
                    PointsOwed = debtPoints,
                    Status = "open",
                    CreatedAt = now
                }).ExecuteCommandAsync(cancellationToken);
                continue;
            }

            var pointsOwedBefore = debt.PointsOwed;
            var debtUpdated = await db.Updateable<AppleIapDebtEntity>()
                .SetColumns(x => new AppleIapDebtEntity
                {
                    PointsOwed = checked(pointsOwedBefore + debtPoints),
                    Status = "open",
                    UpdatedAt = now
                })
                .Where(x => x.Id == debt.Id && x.PointsOwed == pointsOwedBefore)
                .ExecuteCommandAsync(cancellationToken);
            if (debtUpdated != 1)
            {
                throw new AppException(ErrorCodes.Conflict, "Apple 退款欠款状态发生变化，请重试");
            }
        }
    }

    public async Task<PointBucketSummary> GetSummaryAsync(
        long userId,
        int totalBalance,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var buckets = await db.Queryable<PointBucketEntity>()
            .Where(x => x.UserId == userId
                && x.RemainingPoints > 0
                && x.ExpiresAt != null
                && x.ExpiresAt > now)
            .OrderBy(x => x.ExpiresAt)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var expiringPoints = buckets.Aggregate(
            0,
            (total, bucket) => checked(total + bucket.RemainingPoints));
        if (expiringPoints > totalBalance)
        {
            throw new AppException(ErrorCodes.ServerError, "限时积分余额与用户总余额不一致");
        }

        DateTime? nextExpireAt = buckets.Count == 0 ? null : buckets[0].ExpiresAt!.Value;
        var nextExpiringPoints = nextExpireAt.HasValue
            ? buckets.Where(x => x.ExpiresAt == nextExpireAt.Value).Sum(x => x.RemainingPoints)
            : 0;
        return new PointBucketSummary(
            totalBalance - expiringPoints,
            expiringPoints,
            nextExpiringPoints,
            nextExpireAt);
    }

    private static string GetExpirationSource(string source) => source switch
    {
        "sign_in" => "sign_in_expire",
        "recharge" => "recharge_expire",
        "apple_iap" => "apple_iap_expire",
        _ => "point_expire"
    };

    private static string ParseAppleRefundBusinessKey(string businessKey)
    {
        const string prefix = "apple:";
        const string suffix = ":refund";
        if (!businessKey.StartsWith(prefix, StringComparison.Ordinal)
            || !businessKey.EndsWith(suffix, StringComparison.Ordinal)
            || businessKey.Length <= prefix.Length + suffix.Length)
        {
            throw new AppException(ErrorCodes.ServerError, "Apple 延后追扣业务键无效");
        }

        return businessKey[prefix.Length..^suffix.Length];
    }
}

internal sealed record PointConsumption(string BusinessKey, int Points);

internal sealed record PointConsumptionSettlement(
    int RefundCreditPoints,
    IReadOnlyList<DeferredPointClawback> DeferredClawbacks);

internal sealed record DeferredPointClawback(string BusinessKey, int Points);

internal sealed record PointBucketSummary(
    int PermanentPoints,
    int ExpiringPoints,
    int NextExpiringPoints,
    DateTime? NextExpireAt);
