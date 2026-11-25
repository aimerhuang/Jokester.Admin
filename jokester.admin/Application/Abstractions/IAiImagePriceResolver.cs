using jokester.admin.Domain.Entities;

namespace jokester.admin.Application.Abstractions;

public interface IAiImagePriceResolver
{
    Task<ResolvedImagePrice> ResolveAsync(long userId, string modelCode, string resolutionCode, string? qualityCode,
        SysUserEntity? lockedUser, CancellationToken cancellationToken);

    Task<ImageModelAccess> RequireAccessAsync(long userId, string modelCode, SysUserEntity? lockedUser,
        CancellationToken cancellationToken);
}

public sealed record ImageModelAccess(bool IsVip, string? Basis);

public sealed record ResolvedImagePrice(int StandardPoints, int EffectivePoints, string PricingTier, string? AccessBasis);
