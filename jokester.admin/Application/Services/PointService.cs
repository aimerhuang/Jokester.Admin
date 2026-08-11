using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
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

    public async Task<PointBalanceDto> GetBalanceAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        await db.Ado.BeginTranAsync();
        try
        {
            var user = await db.Queryable<SysUserEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken);
            if (user is null)
            {
                throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
            }

            var todayStart = DateTime.Today;
            await ExpirePreviousSignInPointsAsync(user, todayStart, cancellationToken);
            var tomorrowStart = todayStart.AddDays(1);
            var hasSignedInToday = await HasSignedInAsync(userId, todayStart, tomorrowStart, cancellationToken);

            await db.Ado.CommitTranAsync();
            return new PointBalanceDto
            {
                AvailablePoints = user.PointBalance,
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

    public async Task<SignInPointResponse> SignInAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        var now = DateTime.Now;
        var todayStart = now.Date;
        var tomorrowStart = todayStart.AddDays(1);
        var expireAt = tomorrowStart.AddTicks(-1);

        await db.Ado.BeginTranAsync();
        try
        {
            var user = await db.Queryable<SysUserEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken);
            if (user is null)
            {
                throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
            }

            await ExpirePreviousSignInPointsAsync(user, todayStart, cancellationToken);

            if (await HasSignedInAsync(userId, todayStart, tomorrowStart, cancellationToken))
            {
                throw new AppException(ErrorCodes.BadRequest, "今日已签到");
            }

            var balanceAfter = user.PointBalance + SignInGiftPoints;
            await db.Updateable<SysUserEntity>()
                .SetColumns(x => new SysUserEntity
                {
                    PointBalance = balanceAfter,
                    UpdatedAt = now
                })
                .Where(x => x.Id == userId && !x.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);

            await db.Insertable(new UserPointDetailEntity
            {
                UserId = userId,
                ChangePoints = SignInGiftPoints,
                BalanceAfter = balanceAfter,
                ChangeType = "gift",
                Source = SignInSource,
                Remark = $"每日签到赠送积分，有效期至 {expireAt:yyyy-MM-dd HH:mm:ss}",
                CreatedAt = now
            }).ExecuteCommandAsync(cancellationToken);

            await db.Ado.CommitTranAsync();
            return new SignInPointResponse
            {
                Points = SignInGiftPoints,
                ExpireAt = expireAt,
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

        // 部分模型（例如 Nano Banana）官方不支持 quality 参数，quality 仅适用于 gpt-image。
        // 因此只有在调用方显式传入 quality 时才参与价格匹配；未传时忽略库中 quality 列（无论是 '' 还是 NULL）。
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
                .FirstAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken);
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

            await ExpirePreviousSignInPointsAsync(user, DateTime.Today, cancellationToken);
            if (user.PointBalance < totalPointCost)
            {
                throw new AppException(ErrorCodes.BadRequest, $"积分不足，需要 {totalPointCost} 积分，当前可用 {user.PointBalance} 积分");
            }

            foreach (var task in tasks)
            {
                task.Id = await db.Insertable(task).ExecuteReturnBigIdentityAsync();
            }

            var balanceAfter = user.PointBalance - totalPointCost;
            var affected = await db.Updateable<SysUserEntity>()
                .SetColumns(x => new SysUserEntity
                {
                    PointBalance = balanceAfter,
                    UpdatedAt = DateTime.Now
                })
                .Where(x => x.Id == userId && !x.IsDeleted && x.PointBalance >= totalPointCost)
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
                    CreatedAt = DateTime.Now
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

    public async Task<ImageTaskSettlementResult> SettleImageTaskAsync(
        long taskId,
        int finalStatus,
        string? resultUrls,
        string? errorMessage,
        int completedImageCount,
        CancellationToken cancellationToken)
    {
        if (finalStatus is not 1 and not 2)
        {
            throw new ArgumentOutOfRangeException(nameof(finalStatus), "Final AI image task status must be success or failure");
        }

        await db.Ado.BeginTranAsync();
        try
        {
            var task = await db.Queryable<AiImageTaskEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken);
            if (task is null)
            {
                throw new NotFoundException($"AI image task does not exist: {taskId}");
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

            if (refundPoints > 0)
            {
                var user = await db.Queryable<SysUserEntity>()
                    .TranLock(DbLockType.Wait)
                    .FirstAsync(x => x.Id == task.UserId && !x.IsDeleted, cancellationToken);
                if (user is null)
                {
                    throw new NotFoundException($"用户不存在: {task.UserId}");
                }

                var balanceAfter = checked(user.PointBalance + refundPoints);
                await db.Updateable<SysUserEntity>()
                    .SetColumns(x => new SysUserEntity
                    {
                        PointBalance = balanceAfter,
                        UpdatedAt = DateTime.Now
                    })
                    .Where(x => x.Id == task.UserId && !x.IsDeleted)
                    .ExecuteCommandAsync(cancellationToken);

                await db.Insertable(new UserPointDetailEntity
                {
                    UserId = task.UserId,
                    ChangePoints = refundPoints,
                    BalanceAfter = balanceAfter,
                    ChangeType = "refund",
                    Source = ImageRefundSource,
                    BusinessKey = BuildImageBusinessKey(task.Id, "refund"),
                    Remark = $"AI 出图失败返还积分，任务ID：{task.Id}，未完成图片数：{failedImageCount}",
                    CreatedAt = DateTime.Now
                }).ExecuteCommandAsync(cancellationToken);
            }

            var now = HongKongNow();
            var affected = await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = finalStatus,
                    BillingStatus = billingStatus,
                    CompletedImageCount = normalizedCompletedCount,
                    ResultUrls = resultUrls,
                    ErrorMessage = finalStatus == 1 ? null : errorMessage,
                    CompletedAt = now,
                    UpdatedAt = now
                })
                .Where(x => x.Id == task.Id && !x.IsDeleted && x.BillingStatus == 0 && (x.Status == 0 || x.Status == 3))
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
            {
                throw new AppException(ErrorCodes.ServerError, "AI image task settlement state changed unexpectedly");
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

    private async Task ExpirePreviousSignInPointsAsync(SysUserEntity user, DateTime todayStart, CancellationToken cancellationToken)
    {
        var signedInToday = await HasSignedInAsync(user.Id, todayStart, todayStart.AddDays(1), cancellationToken);
        if (signedInToday)
        {
            return;
        }

        var lastSignIn = await db.Queryable<UserPointDetailEntity>()
            .Where(x => x.UserId == user.Id && x.Source == SignInSource && x.ChangePoints > 0 && x.CreatedAt < todayStart)
            .OrderBy(x => x.CreatedAt, OrderByType.Desc)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        if (lastSignIn is null)
        {
            return;
        }

        var alreadyExpired = await db.Queryable<UserPointDetailEntity>()
            .AnyAsync(x => x.UserId == user.Id && x.Source == "sign_in_expire" && x.CreatedAt >= todayStart, cancellationToken);
        if (alreadyExpired)
        {
            return;
        }

        var consumedAfterLastSignIn = await db.Queryable<UserPointDetailEntity>()
            .Where(x => x.UserId == user.Id && x.ChangePoints < 0 && x.CreatedAt >= lastSignIn.CreatedAt && x.CreatedAt < todayStart)
            .SumAsync(x => -x.ChangePoints);
        var remainingSignInPoints = Math.Max(0, SignInGiftPoints - consumedAfterLastSignIn);
        var expirePoints = Math.Min(remainingSignInPoints, user.PointBalance);
        if (expirePoints <= 0)
        {
            return;
        }

        user.PointBalance -= expirePoints;
        await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity
            {
                PointBalance = user.PointBalance,
                UpdatedAt = DateTime.Now
            })
            .Where(x => x.Id == user.Id && !x.IsDeleted)
            .ExecuteCommandAsync(cancellationToken);

        await db.Insertable(new UserPointDetailEntity
        {
            UserId = user.Id,
            ChangePoints = -expirePoints,
            BalanceAfter = user.PointBalance,
            ChangeType = "expire",
            Source = "sign_in_expire",
            Remark = "清除上一日未使用的签到积分",
            CreatedAt = DateTime.Now
        }).ExecuteCommandAsync(cancellationToken);
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

    private static DateTime HongKongNow() => DateTime.UtcNow.AddHours(8);
}
