using System.Diagnostics.Metrics;
using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AiImagePriceResolver(ISqlSugarClient db) : IAiImagePriceResolver
{
    private static readonly Meter Meter = new("Jokester.AiImage.Pricing");
    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>("ai.image.pricing.rejections", "{request}");

    private static void CountRejection(string modelCode, string reason) =>
        Rejections.Add(1, new("model", modelCode), new("reason", reason));

    internal static void CountMissingPrice(string modelCode) =>
        CountRejection(modelCode, MachineErrorCodes.ImagePriceNotConfigured);

    public static string? GetAccessBasis(string level, bool isVip, bool isSuperAdmin) =>
        level == "vip_only" ? isVip ? "vip" : isSuperAdmin ? "super_admin" : null : null;

    public static (int EffectivePoints, string PricingTier) GetEffectivePoints(int standardPoints, int? vipPoints, bool isVip) =>
        isVip && vipPoints.HasValue ? (vipPoints.Value, "vip") : (standardPoints, "standard");

    public async Task<ImageModelAccess> RequireAccessAsync(long userId, string modelCode, SysUserEntity? lockedUser,
        CancellationToken cancellationToken)
    {
        var policyQuery = db.Queryable<AiImageModelPolicyEntity>();
        if (lockedUser is not null)
        {
            // A current read also serializes the first insertion of a policy under InnoDB repeatable read.
            policyQuery = policyQuery.TranLock(DbLockType.Wait);
        }
        var policy = await policyQuery.FirstAsync(x => x.ModelCode == modelCode, cancellationToken);
        var user = lockedUser ?? await db.Queryable<SysUserEntity>()
            .FirstAsync(x => x.Id == userId && !x.IsDeleted && x.Status == 1, cancellationToken)
            ?? throw new NotFoundException($"用户不存在: {userId}");
        var isVip = await new MembershipEntitlementLedger(db).GetActiveAsync(userId, DateTime.Now, cancellationToken) is not null;
        var level = policy?.AccessLevel ?? "all";
        var basis = GetAccessBasis(level, isVip, user.IsSuperAdmin);
        if (level == "vip_only" && basis is null)
        {
            CountRejection(modelCode, MachineErrorCodes.VipRequired);
            throw new AppException(ErrorCodes.Forbidden, MachineErrorCodes.VipRequired, "该模型为 VIP 专属，开通月卡后可用");
        }
        return new ImageModelAccess(isVip, basis);
    }

    public async Task<ResolvedImagePrice> ResolveAsync(long userId, string modelCode, string resolutionCode, string? qualityCode,
        SysUserEntity? lockedUser, CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(userId, modelCode, lockedUser, cancellationToken);
        var query = db.Queryable<AiImagePointPriceEntity>();
        if (lockedUser is not null) query = query.TranLock(DbLockType.Wait);
        var price = await query.Where(x => !x.IsDeleted && x.Status == 1 && x.ModelCode == modelCode && x.ResolutionCode == resolutionCode)
            .WhereIF(!string.IsNullOrEmpty(qualityCode), x => x.QualityCode == qualityCode)
            .FirstAsync(cancellationToken);
        if (price is null || price.Points <= 0)
        {
            CountMissingPrice(modelCode);
            throw new AppException(ErrorCodes.BadRequest, "当前模型、分辨率、画质未配置积分价格");
        }
        var (points, tier) = GetEffectivePoints(price.Points, price.VipPoints, access.IsVip);
        return new ResolvedImagePrice(price.Points, points, tier, access.Basis);
    }

    internal static void EnsureExpectedPrice(string modelCode, int? ceiling, int effectivePoints, string pricingTier)
    {
        if (ceiling.HasValue && effectivePoints > ceiling.Value)
        {
            CountRejection(modelCode, MachineErrorCodes.PriceChanged);
            throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.PriceChanged, $"价格已更新为 {effectivePoints} 积分，请确认后重试",
                new { effectivePoints, pricingTier });
        }
    }
}
