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
    private const string SignInExpireSource = "sign_in_expire";
    private const string ImageGenerateSource = "image_generate";
    private const string ImageRefundSource = "image_refund";

    public async Task<PointBalanceDto> GetBalanceAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        var user = await db.Queryable<SysUserEntity>()
            .FirstAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken);
        if (user is null)
        {
            throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        }

        var todayStart = DateTime.Today;
        await ExpirePreviousSignInPointsAsync(user, todayStart, cancellationToken);
        var tomorrowStart = todayStart.AddDays(1);
        var hasSignedInToday = await HasSignedInAsync(userId, todayStart, tomorrowStart, cancellationToken);

        return new PointBalanceDto
        {
            AvailablePoints = user.PointBalance,
            HasSignedInToday = hasSignedInToday,
            TodaySignInPoints = hasSignedInToday ? SignInGiftPoints : 0
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

            await db.Insertable(new UserPointDetailEntity
            {
                UserId = userId,
                ChangePoints = SignInGiftPoints,
                BalanceAfter = balanceAfter,
                ChangeType = "gift",
                Source = SignInSource,
                BizKey = signInKey,
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

    public async Task ConsumeForImageAsync(long userId, long taskId, string modelCode, string resolutionCode, string qualityCode, int points, CancellationToken cancellationToken)
    {
        if (points <= 0)
        {
            return;
        }

        var now = DateTime.Now;
        var bizKey = BuildImageBizKey(ImageGenerateSource, taskId);
        await db.Ado.BeginTranAsync();
        try
        {
            var user = await db.Queryable<SysUserEntity>()
                .FirstAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken);
            if (user is null)
            {
                throw new NotFoundException($"用户不存在: {userId}");
            }

            if (taskId > 0)
            {
                var alreadyConsumed = await db.Queryable<UserPointDetailEntity>()
                    .AnyAsync(x => x.UserId == userId && x.Source == ImageGenerateSource && x.BizKey == bizKey, cancellationToken);
                if (alreadyConsumed)
                {
                    await db.Ado.CommitTranAsync();
                    return;
                }
            }

            await ExpirePreviousSignInPointsAsync(user, DateTime.Today, cancellationToken);
            if (user.PointBalance < points)
            {
                throw new AppException(ErrorCodes.BadRequest, $"积分不足，需要 {points} 积分，当前可用 {user.PointBalance} 积分");
            }

            var balanceAfter = user.PointBalance - points;
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
                throw new AppException(ErrorCodes.BadRequest, "积分不足，请刷新后重试");
            }

            await db.Insertable(new UserPointDetailEntity
            {
                UserId = userId,
                ChangePoints = -points,
                BalanceAfter = balanceAfter,
                ChangeType = "consume",
                Source = ImageGenerateSource,
                BizKey = bizKey,
                Remark = $"AI 出图扣除积分，任务ID：{taskId}，模型：{modelCode}，分辨率：{resolutionCode}，画质：{qualityCode}",
                CreatedAt = now
            }).ExecuteCommandAsync(cancellationToken);

            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task RefundForImageAsync(long userId, long taskId, int points, CancellationToken cancellationToken)
    {
        if (points <= 0)
        {
            return;
        }

        var now = DateTime.Now;
        var bizKey = BuildImageBizKey(ImageRefundSource, taskId);
        await db.Ado.BeginTranAsync();
        try
        {
            var user = await db.Queryable<SysUserEntity>()
                .FirstAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken);
            if (user is null)
            {
                throw new NotFoundException($"用户不存在: {userId}");
            }

            if (taskId > 0)
            {
                var alreadyRefunded = await db.Queryable<UserPointDetailEntity>()
                    .AnyAsync(x => x.UserId == userId && x.Source == ImageRefundSource && x.BizKey == bizKey, cancellationToken);
                if (alreadyRefunded)
                {
                    await db.Ado.CommitTranAsync();
                    return;
                }
            }

            var balanceAfter = user.PointBalance + points;
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
                throw new AppException(ErrorCodes.ServerError, "积分返还失败，请重试");
            }

            await db.Insertable(new UserPointDetailEntity
            {
                UserId = userId,
                ChangePoints = points,
                BalanceAfter = balanceAfter,
                ChangeType = "refund",
                Source = ImageRefundSource,
                BizKey = bizKey,
                Remark = $"AI 出图失败返还积分，任务ID：{taskId}",
                CreatedAt = now
            }).ExecuteCommandAsync(cancellationToken);

            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
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
            .AnyAsync(x => x.UserId == user.Id && x.Source == SignInExpireSource && x.CreatedAt >= todayStart, cancellationToken);
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
        var affected = await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity
            {
                PointBalance = user.PointBalance,
                UpdatedAt = DateTime.Now
            })
            .Where(x => x.Id == user.Id && !x.IsDeleted && x.PointBalance >= expirePoints)
            .ExecuteCommandAsync(cancellationToken);
        if (affected == 0)
        {
            throw new AppException(ErrorCodes.ServerError, "积分过期处理失败，请重试");
        }

        await db.Insertable(new UserPointDetailEntity
        {
            UserId = user.Id,
            ChangePoints = -expirePoints,
            BalanceAfter = user.PointBalance,
            ChangeType = "expire",
            Source = SignInExpireSource,
            BizKey = BuildSignInExpireBizKey(user.Id, todayStart),
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

    private static string BuildSignInBizKey(long userId, DateTime todayStart)
    {
        return $"sign-in:{userId}:{todayStart:yyyyMMdd}";
    }

    private static string BuildSignInExpireBizKey(long userId, DateTime todayStart)
    {
        return $"sign-in-expire:{userId}:{todayStart:yyyyMMdd}";
    }

    private static string BuildImageBizKey(string source, long taskId)
    {
        return taskId > 0 ? $"{source}:{taskId}" : $"{source}:0";
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
}

