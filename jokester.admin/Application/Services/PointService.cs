using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.DTOs.Common;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class PointService(ISqlSugarClient db, ICurrentUser currentUser) : IPointService
{
    private const int SignInGiftPoints = 25;
    private const string SignInSource = "sign_in";
    private const string ImageGenerateSource = "image_generate";
    private const string ImageRefundSource = "image_refund";
    private readonly PointBucketLedger _pointBuckets = new(db);

    public async Task<PointBalanceDto> GetBalanceAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        await db.Ado.BeginTranAsync();
        try
        {
            var user = await db.Queryable<SysUserEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == userId && !x.IsDeleted && x.Status == 1, cancellationToken);
            if (user is null)
            {
                throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
            }

            var now = DateTime.Now;
            var todayStart = now.Date;
            await _pointBuckets.ExpireAsync(user, now, cancellationToken);
            await _pointBuckets.ExpireLegacyPreviousSignInPointsAsync(user, todayStart, cancellationToken);
            var tomorrowStart = todayStart.AddDays(1);
            var hasSignedInToday = await HasSignedInAsync(userId, todayStart, tomorrowStart, cancellationToken);
            var summary = await _pointBuckets.GetSummaryAsync(userId, user.PointBalance, now, cancellationToken);

            await db.Ado.CommitTranAsync();
            return new PointBalanceDto
            {
                AvailablePoints = user.PointBalance,
                PermanentPoints = summary.PermanentPoints,
                ExpiringPoints = summary.ExpiringPoints,
                NextExpiringPoints = summary.NextExpiringPoints,
                NextExpireAt = summary.NextExpireAt.HasValue
                    ? ApiDateTime.FromLocalStorage(summary.NextExpireAt.Value)
                    : null,
                HasSignedInToday = hasSignedInToday,
                TodaySignInPoints = hasSignedInToday ? SignInGiftPoints : 0
            };
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<PagedResult<PointDetailDto>> GetDetailsAsync(
        PageQuery query,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new AppException(
                ErrorCodes.Unauthorized,
                MachineErrorCodes.Unauthorized,
                "User is not authenticated");
        var exists = await db.Queryable<SysUserEntity>()
            .AnyAsync(x => x.Id == userId && !x.IsDeleted && x.Status == 1, cancellationToken);
        if (!exists)
        {
            throw new AppException(
                ErrorCodes.Unauthorized,
                MachineErrorCodes.SessionRevoked,
                "The login session has been revoked.");
        }

        RefAsync<int> total = 0;
        var entities = await db.Queryable<UserPointDetailEntity>()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAt, OrderByType.Desc)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .ToPageListAsync(query.PageIndex, query.PageSize, total);
        var items = entities.Select(x => new PointDetailDto
        {
            Id = x.Id,
            ChangePoints = x.ChangePoints,
            BalanceAfter = x.BalanceAfter,
            ChangeType = x.ChangeType,
            Source = x.Source,
            Remark = x.Remark,
            CreatedAt = ApiDateTime.FromLocalStorage(x.CreatedAt)
        }).ToArray();

        return new PagedResult<PointDetailDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<SignInPointResponse> SignInAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        var now = DateTime.Now;
        var todayStart = now.Date;
        var tomorrowStart = todayStart.AddDays(1);
        var expireAt = tomorrowStart.AddTicks(-1);
        var signInKey = BuildSignInBizKey(userId, todayStart);

        await db.Ado.BeginTranAsync();
        try
        {
            var user = await db.Queryable<SysUserEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == userId && !x.IsDeleted && x.Status == 1, cancellationToken);
            if (user is null)
            {
                throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
            }

            await _pointBuckets.ExpireAsync(user, now, cancellationToken);
            await _pointBuckets.ExpireLegacyPreviousSignInPointsAsync(user, todayStart, cancellationToken);

            if (await HasSignedInAsync(userId, todayStart, tomorrowStart, cancellationToken))
            {
                throw new AppException(ErrorCodes.BadRequest, "今日已签到");
            }

            var balanceAfter = user.PointBalance + SignInGiftPoints;
            var affected = await db.Updateable<SysUserEntity>()
                .SetColumns(x => new SysUserEntity
                {
                    PointBalance = balanceAfter,
                    UpdatedAt = now
                })
                .Where(x => x.Id == userId && !x.IsDeleted && x.PointBalance == user.PointBalance)
                .ExecuteCommandAsync(cancellationToken);
            if (affected == 0)
            {
                throw new AppException(ErrorCodes.BadRequest, "今日已签到");
            }

            await _pointBuckets.GrantAsync(
                userId,
                SignInGiftPoints,
                SignInSource,
                signInKey,
                tomorrowStart,
                PointBucketLedger.SignInSpendPriority,
                now,
                cancellationToken);

            await db.Insertable(new UserPointDetailEntity
            {
                UserId = userId,
                ChangePoints = SignInGiftPoints,
                BalanceAfter = balanceAfter,
                ChangeType = "gift",
                Source = SignInSource,
                BusinessKey = signInKey,
                Remark = $"每日签到赠送积分，有效期至 {expireAt:yyyy-MM-dd HH:mm:ss}",
                CreatedAt = now
            }).ExecuteCommandAsync(cancellationToken);

            await db.Ado.CommitTranAsync();
            return new SignInPointResponse
            {
                Points = SignInGiftPoints,
                ExpireAt = ApiDateTime.FromLocalStorage(expireAt),
                AvailablePoints = balanceAfter
            };
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<int> GetImageGenerateCostAsync(string modelCode, string resolutionCode, string qualityCode, int imageCount, CancellationToken cancellationToken)
    {
        if (imageCount <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Image count must be greater than 0");
        }

        var normalizedModelCode = NormalizeRequired(modelCode, "Model code is required");
        var normalizedResolutionCode = NormalizeRequired(resolutionCode, "Resolution code is required");
        var normalizedQualityCode = NormalizeOptional(qualityCode);

        var matchQuality = !string.IsNullOrEmpty(normalizedQualityCode);

        var price = await db.Queryable<AiImagePointPriceEntity>()
            .Where(x => !x.IsDeleted
                && x.Status == 1
                && x.ModelCode == normalizedModelCode
                && x.ResolutionCode == normalizedResolutionCode)
            .WhereIF(matchQuality, x => x.QualityCode == normalizedQualityCode)
            .FirstAsync(cancellationToken);
        if (price is null || price.Points <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "当前模型、分辨率、画质未配置积分价格");
        }

        return price.Points * imageCount;
    }

    public async Task<ImageTaskReservationResult> ReserveImageTaskAsync(
        AiImageTaskEntity task,
        string modelCode,
        string resolutionCode,
        string qualityCode,
        CancellationToken cancellationToken)
    {
        var result = await ReserveImageTasksAsync(
            [task],
            modelCode,
            resolutionCode,
            qualityCode,
            cancellationToken);
        return new ImageTaskReservationResult(result.TaskIds[0], result.Created);
    }

    public async Task<ImageTaskBatchReservationResult> ReserveImageTasksAsync(
        IReadOnlyList<AiImageTaskEntity> tasks,
        string modelCode,
        string resolutionCode,
        string qualityCode,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "At least one AI image task is required");
        }

        var userId = tasks[0].UserId;
        if (userId <= 0
            || tasks.Any(task => task.UserId != userId || task.PointCost <= 0 || task.ImageCount <= 0))
        {
            throw new AppException(ErrorCodes.BadRequest, "AI image task billing snapshot is invalid");
        }
        if (tasks.Any(task => string.IsNullOrWhiteSpace(task.IdempotencyKey)
                || string.IsNullOrWhiteSpace(task.RequestFingerprint))
            || tasks.Select(task => task.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != tasks.Count)
        {
            throw new AppException(ErrorCodes.BadRequest, "AI image task idempotency snapshot is invalid");
        }

        var totalPointCost = tasks.Aggregate(0, (total, task) => checked(total + task.PointCost));
        var idempotencyKeys = tasks.Select(task => task.IdempotencyKey).ToArray();
        await db.Ado.BeginTranAsync();
        try
        {
            var user = await db.Queryable<SysUserEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == userId && !x.IsDeleted && x.Status == 1, cancellationToken);
            if (user is null)
            {
                throw new NotFoundException($"用户不存在: {userId}");
            }

            var existingTasks = await db.Queryable<AiImageTaskEntity>()
                .Where(x => x.UserId == userId && idempotencyKeys.Contains(x.IdempotencyKey))
                .ToListAsync(cancellationToken);
            if (existingTasks.Count > 0)
            {
                var existingLookup = existingTasks.ToDictionary(x => x.IdempotencyKey, StringComparer.Ordinal);
                var orderedExistingIds = new List<long>(tasks.Count);
                foreach (var task in tasks)
                {
                    if (!existingLookup.TryGetValue(task.IdempotencyKey, out var existing)
                        || !string.Equals(existing.RequestFingerprint, task.RequestFingerprint, StringComparison.Ordinal))
                    {
                        throw new ConflictException("Idempotency key was already used with a different request");
                    }

                    orderedExistingIds.Add(existing.Id);
                }

                await db.Ado.CommitTranAsync();
                return new ImageTaskBatchReservationResult(orderedExistingIds, false);
            }

            var hasAppleDebt = await db.Queryable<AppleIapDebtEntity>()
                .AnyAsync(x => x.UserId == userId && x.Status == "open" && x.PointsOwed > 0, cancellationToken);
            if (hasAppleDebt)
            {
                throw new AppException(
                    ErrorCodes.Forbidden,
                    MachineErrorCodes.ResourceForbidden,
                    "AI image generation is unavailable while an Apple IAP refund balance is outstanding.");
            }

            var pendingTaskIds = await db.Queryable<AiImageTaskEntity>()
                .Where(x => x.UserId == userId
                    && !x.IsDeleted
                    && x.BillingStatus == 0
                    && (x.Status == 0 || x.Status == 3))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            if (pendingTaskIds.Count > 0)
            {
                var pendingBusinessKeys = pendingTaskIds
                    .Select(id => BuildImageBusinessKey(id, "reserve"))
                    .ToArray();
                var hasDeferredAppleClawback = await db.Queryable<PointBucketUsageEntity>()
                    .AnyAsync(x => x.UserId == userId
                        && x.DeferredClawbackPoints > 0
                        && pendingBusinessKeys.Contains(x.BusinessKey),
                        cancellationToken);
                if (hasDeferredAppleClawback)
                {
                    throw new AppException(
                        ErrorCodes.Forbidden,
                        MachineErrorCodes.ResourceForbidden,
                        "AI image generation is unavailable while an Apple refund is awaiting task settlement.");
                }
            }

            var billingNow = DateTime.Now;
            await _pointBuckets.ExpireAsync(user, billingNow, cancellationToken);
            await _pointBuckets.ExpireLegacyPreviousSignInPointsAsync(user, billingNow.Date, cancellationToken);
            if (user.PointBalance < totalPointCost)
            {
                throw new AppException(ErrorCodes.BadRequest, $"积分不足，需要 {totalPointCost} 积分，当前可用 {user.PointBalance} 积分");
            }

            foreach (var task in tasks)
            {
                task.Id = await db.Insertable(task).ExecuteReturnBigIdentityAsync();
            }

            await _pointBuckets.AllocateAsync(
                user,
                tasks.Select(task => new PointConsumption(
                    BuildImageBusinessKey(task.Id, "reserve"),
                    task.PointCost)).ToArray(),
                billingNow,
                cancellationToken);

            var balanceAfter = user.PointBalance - totalPointCost;
            var affected = await db.Updateable<SysUserEntity>()
                .SetColumns(x => new SysUserEntity
                {
                    PointBalance = balanceAfter,
                    UpdatedAt = billingNow
                })
                .Where(x => x.Id == userId && !x.IsDeleted && x.PointBalance == user.PointBalance)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
            {
                throw new AppException(ErrorCodes.BadRequest, "积分不足或积分余额已发生变化，请重试");
            }

            var runningBalance = user.PointBalance;
            var pointDetails = tasks.Select(task =>
            {
                runningBalance -= task.PointCost;
                return new UserPointDetailEntity
                {
                    UserId = task.UserId,
                    ChangePoints = -task.PointCost,
                    BalanceAfter = runningBalance,
                    ChangeType = "consume",
                    Source = ImageGenerateSource,
                    BusinessKey = BuildImageBusinessKey(task.Id, "reserve"),
                    Remark = $"AI 出图预留积分，任务ID：{task.Id}，模型：{modelCode}，分辨率：{resolutionCode}，画质：{qualityCode}",
                    CreatedAt = billingNow
                };
            }).ToArray();
            await db.Insertable(pointDetails).ExecuteCommandAsync(cancellationToken);

            await db.Ado.CommitTranAsync();
            return new ImageTaskBatchReservationResult(tasks.Select(task => task.Id).ToArray(), true);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<VersionedImageTaskBatchReservationResult> ReserveVersionedImageTasksAsync(
        AiImageRequestEntity request,
        IReadOnlyList<AiImageTaskEntity> tasks,
        IReadOnlyList<AiImageTaskInputEntity> inputs,
        long priceId,
        CancellationToken cancellationToken)
    {
        if (request.UserId <= 0 || tasks.Count == 0 || request.RequestedImageCount != tasks.Count
            || tasks.Any(task => task.UserId != request.UserId || task.ImageCount != 1)
            || inputs.Any(input => input.OwnerUserId != request.UserId
                || input.RequestTaskOrdinal < 0
                || input.RequestTaskOrdinal >= tasks.Count)
            || string.IsNullOrWhiteSpace(request.IdempotencyKeyHash)
            || string.IsNullOrWhiteSpace(request.CanonicalPayloadHash))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Versioned AI image billing snapshot is invalid.");
        }

        await db.Ado.BeginTranAsync();
        try
        {
            var user = await db.Queryable<SysUserEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == request.UserId && !x.IsDeleted && x.Status == 1, cancellationToken)
                ?? throw new NotFoundException($"用户不存在: {request.UserId}");

            var existingRequest = await db.Queryable<AiImageRequestEntity>()
                .FirstAsync(x => x.UserId == request.UserId && x.IdempotencyKeyHash == request.IdempotencyKeyHash, cancellationToken);
            if (existingRequest is not null)
            {
                if (!string.Equals(existingRequest.CanonicalPayloadHash, request.CanonicalPayloadHash, StringComparison.Ordinal))
                {
                    throw new AppException(
                        ErrorCodes.Conflict,
                        MachineErrorCodes.IdempotencyConflict,
                        "The idempotency key was already used with a different AI image request.");
                }
                var existingIds = await db.Queryable<AiImageRequestTaskEntity>()
                    .Where(x => x.RequestId == existingRequest.Id)
                    .OrderBy(x => x.TaskOrdinal)
                    .Select(x => x.TaskId)
                    .ToListAsync(cancellationToken);
                if (existingIds.Count != existingRequest.TaskCount)
                {
                    throw new AppException(
                        ErrorCodes.Conflict,
                        MachineErrorCodes.LegacyIdempotencyUnverifiable,
                        "The durable AI image request batch is incomplete.");
                }
                await db.Ado.CommitTranAsync();
                return new VersionedImageTaskBatchReservationResult(existingRequest.Id, existingIds, false);
            }

            var price = await db.Queryable<AiImageModelReleasePriceEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == priceId && x.Status == 1 && x.Points > 0, cancellationToken);
            if (price is null || request.ModelReleaseId != price.ModelReleaseId)
            {
                throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.ImageCatalogChanged, "图片模型目录已更新，请刷新后重试");
            }
            var release = await db.Queryable<AiImageModelReleaseEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == price.ModelReleaseId && x.Status == "published" && x.RevokedAt == null, cancellationToken);
            var pointer = await db.Queryable<AiImageCurrentReleaseEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.ModelCode == price.ModelCode && x.ModelReleaseId == price.ModelReleaseId, cancellationToken);
            if (release is null || pointer is null)
            {
                throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.ImageCatalogChanged, "图片模型目录已更新，请刷新后重试");
            }

            foreach (var task in tasks)
            {
                task.PointCost = price.Points;
                task.UnitPointCost = price.Points;
                task.PriceId = price.Id;
                task.PriceReleaseId = price.ModelReleaseId;
            }
            request.ReservedPointCost = checked(price.Points * tasks.Count);
            request.TaskCount = tasks.Count;
            var totalPointCost = request.ReservedPointCost;

            var hasAppleDebt = await db.Queryable<AppleIapDebtEntity>()
                .AnyAsync(x => x.UserId == request.UserId && x.Status == "open" && x.PointsOwed > 0, cancellationToken);
            if (hasAppleDebt)
            {
                throw new AppException(ErrorCodes.Forbidden, MachineErrorCodes.ResourceForbidden,
                    "AI image generation is unavailable while an Apple IAP refund balance is outstanding.");
            }
            var pendingTaskIds = await db.Queryable<AiImageTaskEntity>()
                .Where(x => x.UserId == request.UserId && !x.IsDeleted && x.BillingStatus == 0 && (x.Status == 0 || x.Status == 3))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            if (pendingTaskIds.Count > 0)
            {
                var pendingBusinessKeys = pendingTaskIds.Select(id => BuildImageBusinessKey(id, "reserve")).ToArray();
                var deferred = await db.Queryable<PointBucketUsageEntity>()
                    .AnyAsync(x => x.UserId == request.UserId && x.DeferredClawbackPoints > 0
                        && pendingBusinessKeys.Contains(x.BusinessKey), cancellationToken);
                if (deferred)
                {
                    throw new AppException(ErrorCodes.Forbidden, MachineErrorCodes.ResourceForbidden,
                        "AI image generation is unavailable while an Apple refund is awaiting task settlement.");
                }
            }

            var billingNow = DateTime.Now;
            await _pointBuckets.ExpireAsync(user, billingNow, cancellationToken);
            await _pointBuckets.ExpireLegacyPreviousSignInPointsAsync(user, billingNow.Date, cancellationToken);
            if (user.PointBalance < totalPointCost)
            {
                throw new AppException(ErrorCodes.BadRequest, $"积分不足，需要 {totalPointCost} 积分，当前可用 {user.PointBalance} 积分");
            }

            request.Id = await db.Insertable(request).ExecuteReturnBigIdentityAsync();
            foreach (var task in tasks)
            {
                task.Id = await db.Insertable(task).ExecuteReturnBigIdentityAsync();
            }
            if (inputs.Count > 0)
            {
                foreach (var input in inputs)
                {
                    input.TaskId = tasks[input.RequestTaskOrdinal].Id;
                }
                await db.Insertable(inputs.ToArray()).ExecuteCommandAsync(cancellationToken);
            }
            var taskLines = tasks.Select((task, ordinal) => new AiImageRequestTaskEntity
            {
                RequestId = request.Id,
                TaskOrdinal = ordinal,
                TaskId = task.Id
            }).ToArray();
            await db.Insertable(taskLines).ExecuteCommandAsync(cancellationToken);
            await db.Insertable(tasks.Select(task => new AiImageTaskOutboxEntity
            {
                RequestId = request.Id,
                TaskId = task.Id,
                Status = "pending",
                NextAttemptAt = billingNow,
                CreatedAt = billingNow
            }).ToArray()).ExecuteCommandAsync(cancellationToken);

            await _pointBuckets.AllocateAsync(
                user,
                tasks.Select(task => new PointConsumption(BuildImageBusinessKey(task.Id, "reserve"), task.PointCost)).ToArray(),
                billingNow,
                cancellationToken);
            var balanceAfter = user.PointBalance - totalPointCost;
            var affected = await db.Updateable<SysUserEntity>()
                .SetColumns(x => new SysUserEntity { PointBalance = balanceAfter, UpdatedAt = billingNow })
                .Where(x => x.Id == request.UserId && !x.IsDeleted && x.PointBalance == user.PointBalance)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
            {
                throw new AppException(ErrorCodes.BadRequest, "积分不足或积分余额已发生变化，请重试");
            }

            var runningBalance = user.PointBalance;
            var pointDetails = tasks.Select(task =>
            {
                runningBalance -= task.PointCost;
                return new UserPointDetailEntity
                {
                    UserId = task.UserId,
                    ChangePoints = -task.PointCost,
                    BalanceAfter = runningBalance,
                    ChangeType = "consume",
                    Source = ImageGenerateSource,
                    BusinessKey = BuildImageBusinessKey(task.Id, "reserve"),
                    Remark = $"AI 出图预留积分，任务ID：{task.Id}，模型：{task.ModelCode}，尺寸模式：{task.SizeMode}，目录：{release.CatalogVersion}",
                    CreatedAt = billingNow
                };
            }).ToArray();
            await db.Insertable(pointDetails).ExecuteCommandAsync(cancellationToken);

            await db.Ado.CommitTranAsync();
            return new VersionedImageTaskBatchReservationResult(request.Id, tasks.Select(x => x.Id).ToArray(), true);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<ImageTaskSettlementResult> SettleImageTaskAsync(
        long taskId,
        int finalStatus,
        string? resultUrls,
        string? errorMessage,
        int completedImageCount,
        CancellationToken cancellationToken) =>
        await SettleImageTaskCoreAsync(
            taskId,
            finalStatus,
            resultUrls,
            errorMessage,
            completedImageCount,
            null,
            cancellationToken);

    public async Task<ImageTaskSettlementResult> SettleVersionedImageTaskAsync(
        long taskId,
        int finalStatus,
        VersionedImageTaskSettlement settlement,
        string? errorMessage,
        int completedImageCount,
        CancellationToken cancellationToken) =>
        await SettleImageTaskCoreAsync(
            taskId,
            finalStatus,
            settlement.ResultUrls,
            errorMessage,
            completedImageCount,
            settlement,
            cancellationToken);

    private async Task<ImageTaskSettlementResult> SettleImageTaskCoreAsync(
        long taskId,
        int finalStatus,
        string? resultUrls,
        string? errorMessage,
        int completedImageCount,
        VersionedImageTaskSettlement? versioned,
        CancellationToken cancellationToken)
    {
        if (finalStatus is not 1 and not 2)
        {
            throw new ArgumentOutOfRangeException(nameof(finalStatus), "Final AI image task status must be success or failure");
        }

        await db.Ado.BeginTranAsync();
        try
        {
            var taskSnapshot = await db.Queryable<AiImageTaskEntity>()
                .FirstAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken);
            if (taskSnapshot is null)
            {
                throw new NotFoundException($"AI image task does not exist: {taskId}");
            }

            // Point mutations consistently lock the user before bucket usages. This
            // also serializes settlement with an Apple refund for the same account.
            var user = await db.Queryable<SysUserEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == taskSnapshot.UserId && !x.IsDeleted, cancellationToken);
            if (user is null)
            {
                throw new NotFoundException($"用户不存在: {taskSnapshot.UserId}");
            }

            var task = await db.Queryable<AiImageTaskEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken);
            if (task is null || task.UserId != user.Id)
            {
                throw new AppException(ErrorCodes.ServerError, "AI image task changed during settlement");
            }

            if (task.ImageCount <= 0)
            {
                throw new AppException(ErrorCodes.ServerError, "AI image task image count snapshot is invalid");
            }
            var normalizedCompletedCount = Math.Clamp(completedImageCount, 0, task.ImageCount);
            if (task.BillingStatus != 0 || task.Status is 1 or 2)
            {
                await db.Ado.CommitTranAsync();
                return new ImageTaskSettlementResult(
                    task.Id,
                    task.UserId,
                    task.ImageCount,
                    task.CompletedImageCount,
                    0,
                    false);
            }
            if (finalStatus == 1 && normalizedCompletedCount != task.ImageCount)
            {
                throw new AppException(ErrorCodes.ServerError, "A successful AI image task must contain all requested images");
            }

            var failedImageCount = task.ImageCount - normalizedCompletedCount;
            if (task.PointCost % task.ImageCount != 0)
            {
                throw new AppException(ErrorCodes.ServerError, "AI image task point snapshot cannot be settled per image");
            }
            var pointCostPerImage = task.ImageCount > 0 ? task.PointCost / task.ImageCount : 0;
            var refundPoints = finalStatus == 2 ? checked(pointCostPerImage * failedImageCount) : 0;
            var billingStatus = finalStatus == 1 || refundPoints == 0
                ? 1
                : normalizedCompletedCount == 0 ? 3 : 2;

            var settlementNow = DateTime.Now;
            await _pointBuckets.ExpireAsync(user, settlementNow, cancellationToken);
            var pointSettlement = await _pointBuckets.SettleConsumptionAsync(
                task.UserId,
                BuildImageBusinessKey(task.Id, "reserve"),
                task.PointCost,
                refundPoints,
                settlementNow,
                cancellationToken);

            if (pointSettlement.RefundCreditPoints > 0)
            {
                var balanceAfter = checked(user.PointBalance + pointSettlement.RefundCreditPoints);
                var balanceAffected = await db.Updateable<SysUserEntity>()
                    .SetColumns(x => new SysUserEntity
                    {
                        PointBalance = balanceAfter,
                        UpdatedAt = settlementNow
                    })
                    .Where(x => x.Id == task.UserId && !x.IsDeleted && x.PointBalance == user.PointBalance)
                    .ExecuteCommandAsync(cancellationToken);
                if (balanceAffected != 1)
                {
                    throw new AppException(ErrorCodes.ServerError, "积分返还失败，请重试");
                }
                user.PointBalance = balanceAfter;
            }

            if (refundPoints > 0)
            {
                var suppressedPoints = refundPoints - pointSettlement.RefundCreditPoints;
                await db.Insertable(new UserPointDetailEntity
                {
                    UserId = task.UserId,
                    ChangePoints = pointSettlement.RefundCreditPoints,
                    BalanceAfter = user.PointBalance,
                    ChangeType = "refund",
                    Source = ImageRefundSource,
                    BusinessKey = BuildImageBusinessKey(task.Id, "refund"),
                    Remark = suppressedPoints > 0
                        ? $"AI 出图失败结算，任务ID：{task.Id}，应退：{refundPoints}，到账：{pointSettlement.RefundCreditPoints}，Apple 退款撤销：{suppressedPoints}"
                        : $"AI 出图失败返还积分，任务ID：{task.Id}，未完成图片数：{failedImageCount}",
                    CreatedAt = settlementNow
                }).ExecuteCommandAsync(cancellationToken);

                // A refund keeps the original bucket expiry. If it expired while the task
                // was running, expire the restored points in the same transaction.
                await _pointBuckets.ExpireAsync(user, settlementNow, cancellationToken);
            }

            await _pointBuckets.ExpireLegacyPreviousSignInPointsAsync(user, settlementNow.Date, cancellationToken);
            await _pointBuckets.ApplyDeferredClawbacksAsync(
                user,
                task.Id,
                pointSettlement.DeferredClawbacks,
                settlementNow,
                cancellationToken);

            var now = HongKongNow();
            var affected = await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = finalStatus,
                    BillingStatus = billingStatus,
                    CompletedImageCount = normalizedCompletedCount,
                    ResultUrls = resultUrls,
                    ErrorMessage = finalStatus == 1 ? null : errorMessage,
                    RefundedPoints = task.SizeContractVersion == AiImageCatalogService.SizeContractVersion ? refundPoints : task.RefundedPoints,
                    OutputWidth = versioned == null ? task.OutputWidth : versioned.OutputWidth,
                    OutputHeight = versioned == null ? task.OutputHeight : versioned.OutputHeight,
                    OutputSize = versioned == null ? task.OutputSize : versioned.OutputSize,
                    OutputMimeType = versioned == null ? task.OutputMimeType : versioned.OutputMimeType,
                    Width = versioned != null
                        && task.SizeMode == AiImageCatalogService.AutoSizeMode
                        && versioned.OutputWidth.HasValue
                            ? versioned.OutputWidth.Value
                            : task.Width,
                    Height = versioned != null
                        && task.SizeMode == AiImageCatalogService.AutoSizeMode
                        && versioned.OutputHeight.HasValue
                            ? versioned.OutputHeight.Value
                            : task.Height,
                    Size = versioned != null
                        && task.SizeMode == AiImageCatalogService.AutoSizeMode
                        && !string.IsNullOrWhiteSpace(versioned.OutputSize)
                            ? versioned.OutputSize
                            : task.Size,
                    FailureCode = finalStatus == 1 || versioned == null ? null : versioned.FailureCode,
                    FailureStage = finalStatus == 1 || versioned == null ? null : versioned.FailureStage,
                    Retryable = finalStatus == 1 || versioned == null ? null : versioned.Retryable,
                    ClaimTokenHash = versioned == null ? task.ClaimTokenHash : null,
                    LeaseExpiresAt = versioned == null ? task.LeaseExpiresAt : null,
                    CompletedAt = now,
                    UpdatedAt = now
                })
                .Where(x => x.Id == task.Id && !x.IsDeleted && x.BillingStatus == 0 && (x.Status == 0 || x.Status == 3))
                .WhereIF(versioned is not null, x => x.ClaimEpoch == versioned!.ClaimEpoch && x.ClaimTokenHash == versioned.ClaimTokenHash)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
            {
                throw new AppException(ErrorCodes.ServerError, "AI image task settlement state changed unexpectedly");
            }

            if (versioned is not null && !string.IsNullOrWhiteSpace(versioned.ProviderAttemptId))
            {
                var attemptAffected = await db.Updateable<AiImageProviderAttemptEntity>()
                    .SetColumns(x => new AiImageProviderAttemptEntity
                    {
                        State = versioned.ProviderAttemptState,
                        CompletedAt = now
                    })
                    .Where(x => x.AttemptId == versioned.ProviderAttemptId
                        && x.TaskId == task.Id
                        && x.ClaimEpoch == versioned.ClaimEpoch
                        && (x.State == "prepared" || x.State == "inflight" || x.State == "provider_unknown"))
                    .ExecuteCommandAsync(cancellationToken);
                if (attemptAffected != 1)
                {
                    throw new AppException(ErrorCodes.ServerError, "AI image provider attempt changed during settlement");
                }
            }

            if (versioned is not null
                && finalStatus == 1
                && versioned.OutputWidth is > 0
                && versioned.OutputHeight is > 0
                && !string.IsNullOrWhiteSpace(versioned.OutputSize)
                && !string.IsNullOrWhiteSpace(versioned.OutputMimeType))
            {
                var outputUrl = DeserializeSingleResultUrl(resultUrls);
                if (outputUrl is null)
                {
                    throw new AppException(ErrorCodes.ServerError, "AI image result snapshot is invalid");
                }
                await db.Insertable(new AiImageTaskResultEntity
                {
                    TaskId = task.Id,
                    ResultOrdinal = 0,
                    Url = outputUrl,
                    Width = versioned.OutputWidth.Value,
                    Height = versioned.OutputHeight.Value,
                    Size = versioned.OutputSize,
                    MimeType = versioned.OutputMimeType,
                    IsQuarantined = false,
                    CreatedAt = now
                }).ExecuteCommandAsync(cancellationToken);
            }

            if (task.SourcePromptId.HasValue && normalizedCompletedCount > 0)
            {
                await IncrementSuccessfulPromptGenerationAsync(
                    task.SourcePromptId.Value,
                    normalizedCompletedCount,
                    now,
                    cancellationToken);
            }

            await db.Ado.CommitTranAsync();
            return new ImageTaskSettlementResult(
                task.Id,
                task.UserId,
                task.ImageCount,
                normalizedCompletedCount,
                refundPoints,
                true);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task IncrementSuccessfulPromptGenerationAsync(
        long promptId,
        int completedImageCount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var metricDate = now.Date;
        var inserted = await db.Insertable(new PromptLibraryMetricDailyEntity
            {
                PromptId = promptId,
                MetricDate = metricDate,
                SuccessfulGenerationCount = completedImageCount,
                UpdatedAt = now
            })
            .MySqlIgnore()
            .ExecuteCommandAsync(cancellationToken);
        if (inserted == 1)
        {
            return;
        }

        var updated = await db.Updateable<PromptLibraryMetricDailyEntity>()
            .SetColumns(x => x.SuccessfulGenerationCount == x.SuccessfulGenerationCount + completedImageCount)
            .SetColumns(x => x.UpdatedAt == now)
            .Where(x => x.PromptId == promptId && x.MetricDate == metricDate)
            .ExecuteCommandAsync(cancellationToken);
        if (updated != 1)
        {
            throw new AppException(ErrorCodes.ServerError, "Prompt generation metric could not be updated");
        }
    }

    private Task<bool> HasSignedInAsync(long userId, DateTime todayStart, DateTime tomorrowStart, CancellationToken cancellationToken)
    {
        return db.Queryable<UserPointDetailEntity>()
            .AnyAsync(x => x.UserId == userId
                && x.Source == SignInSource
                && x.ChangePoints > 0
                && x.CreatedAt >= todayStart
                && x.CreatedAt < tomorrowStart,
                cancellationToken);
    }

    private static string BuildSignInBizKey(long userId, DateTime todayStart)
    {
        return $"sign-in:{userId}:{todayStart:yyyyMMdd}";
    }

    private static string NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeRequired(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AppException(ErrorCodes.BadRequest, message);
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string BuildImageBusinessKey(long taskId, string operation) => $"image:{taskId}:{operation}";

    private static string? DeserializeSingleResultUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(value)?.FirstOrDefault();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static DateTime HongKongNow() => DateTime.UtcNow.AddHours(8);
}
