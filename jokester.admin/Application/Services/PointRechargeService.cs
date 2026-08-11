using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class PointRechargeService(ISqlSugarClient db, ICurrentUser currentUser) : IPointRechargeService
{
    private const string RechargeSource = "recharge";

    public async Task<IReadOnlyList<RechargePackageDto>> GetPackagesAsync(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var packages = await db.Queryable<PointRechargePackageEntity>()
            .Where(x => !x.IsDeleted && x.Status == 1)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var redeemedCodes = await db.Queryable<PointRedeemCodeEntity>()
            .Where(x => x.RedeemedByUserId == userId && x.Status == 1 && x.PackageId != null)
            .Select(x => new PointRedeemCodeEntity { PackageId = x.PackageId })
            .ToListAsync(cancellationToken);
        var redeemedPackageIds = redeemedCodes
            .Where(x => x.PackageId.HasValue)
            .Select(x => x.PackageId!.Value)
            .ToHashSet();

        return packages.Select(package =>
        {
            var isFirstPurchaseEligible = package.RepeatPoints.HasValue && !redeemedPackageIds.Contains(package.Id);
            var points = package.RepeatPoints.HasValue && !isFirstPurchaseEligible
                ? package.RepeatPoints.Value
                : package.Points;
            var purchaseUrl = ExpandPurchaseUrl(package.PurchaseUrl, string.Empty, package.PackageCode, userId);

            return new RechargePackageDto
            {
                Code = package.PackageCode,
                Name = package.Name,
                Description = package.Description,
                Points = points,
                PriceAmount = package.PriceAmount,
                Currency = package.Currency,
                ValidityDays = package.ValidityDays,
                BonusPercent = package.BonusPercent,
                BadgeCode = package.BadgeCode,
                IsFeatured = package.IsFeatured,
                IsFirstPurchaseEligible = isFirstPurchaseEligible,
                PurchaseEnabled = purchaseUrl is not null,
                Benefits = ParseBenefits(package.BenefitsJson)
            };
        }).ToArray();
    }

    public async Task<RechargeOrderDto> CreateOrderAsync(
        CreateRechargeOrderRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var packageCode = NormalizePackageCode(request.PackageCode);
        var package = await GetEnabledPackageAsync(packageCode, cancellationToken);
        var points = await GetEffectivePointsAsync(package, userId, cancellationToken);
        var now = DateTime.Now;
        var orderNo = "R" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var purchaseUrl = ExpandPurchaseUrl(package.PurchaseUrl, orderNo, package.PackageCode, userId);
        var order = new PointRechargeOrderEntity
        {
            OrderNo = orderNo,
            UserId = userId,
            PackageId = package.Id,
            PackageCode = package.PackageCode,
            Points = points,
            PriceAmount = package.PriceAmount,
            Currency = package.Currency,
            PurchaseUrl = purchaseUrl,
            Status = 0,
            ExpiresAt = now.AddHours(24),
            CreatedAt = now
        };

        order.Id = await db.Insertable(order).ExecuteReturnBigIdentityAsync();
        return MapOrder(order);
    }

    public async Task<RedeemPointCodeResponse> RedeemAsync(
        RedeemPointCodeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var normalizedCode = PointRedeemCodeSecurity.Normalize(request.Code ?? string.Empty);
        if (normalizedCode.Length is < 8 or > 80)
        {
            throw InvalidRedeemCode();
        }

        var codeHash = PointRedeemCodeSecurity.Hash(normalizedCode);
        var now = DateTime.Now;

        await db.Ado.BeginTranAsync();
        try
        {
            var redeemCode = await db.Queryable<PointRedeemCodeEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.CodeHash == codeHash, cancellationToken);
            if (redeemCode is null || redeemCode.Status != 0 || redeemCode.ExpiresAt <= now)
            {
                throw InvalidRedeemCode();
            }

            var awardedPoints = redeemCode.Points;
            if (redeemCode.PackageId.HasValue)
            {
                var package = await db.Queryable<PointRechargePackageEntity>()
                    .FirstAsync(x => x.Id == redeemCode.PackageId.Value && !x.IsDeleted, cancellationToken);
                if (package?.RepeatPoints is > 0)
                {
                    var hasRedeemedPackage = await db.Queryable<PointRedeemCodeEntity>()
                        .AnyAsync(x => x.Id != redeemCode.Id
                            && x.PackageId == redeemCode.PackageId
                            && x.RedeemedByUserId == userId
                            && x.Status == 1,
                            cancellationToken);
                    if (hasRedeemedPackage)
                    {
                        awardedPoints = package.RepeatPoints.Value;
                    }
                }
            }

            if (awardedPoints <= 0)
            {
                throw InvalidRedeemCode();
            }

            var user = await db.Queryable<SysUserEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == userId && !x.IsDeleted && x.Status == 1, cancellationToken);
            if (user is null)
            {
                throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
            }

            var balanceAfter = checked(user.PointBalance + awardedPoints);
            var codeAffected = await db.Updateable<PointRedeemCodeEntity>()
                .SetColumns(x => new PointRedeemCodeEntity
                {
                    Status = 1,
                    RedeemedByUserId = userId,
                    RedeemedAt = now,
                    UpdatedAt = now
                })
                .Where(x => x.Id == redeemCode.Id && x.Status == 0)
                .ExecuteCommandAsync(cancellationToken);
            if (codeAffected != 1)
            {
                throw InvalidRedeemCode();
            }

            var userAffected = await db.Updateable<SysUserEntity>()
                .SetColumns(x => new SysUserEntity
                {
                    PointBalance = balanceAfter,
                    UpdatedAt = now
                })
                .Where(x => x.Id == userId && !x.IsDeleted && x.Status == 1)
                .ExecuteCommandAsync(cancellationToken);
            if (userAffected != 1)
            {
                throw new AppException(ErrorCodes.ServerError, "Failed to update point balance");
            }

            await db.Insertable(new UserPointDetailEntity
            {
                UserId = userId,
                ChangePoints = awardedPoints,
                BalanceAfter = balanceAfter,
                ChangeType = "recharge",
                Source = RechargeSource,
                BusinessKey = $"recharge:redeem:{redeemCode.Id}",
                Remark = $"积分兑换到账，卡密 {redeemCode.CodeMask}",
                CreatedAt = now
            }).ExecuteCommandAsync(cancellationToken);

            if (redeemCode.OrderId.HasValue)
            {
                await db.Updateable<PointRechargeOrderEntity>()
                    .SetColumns(x => new PointRechargeOrderEntity
                    {
                        Status = 2,
                        FulfilledAt = now,
                        UpdatedAt = now
                    })
                    .Where(x => x.Id == redeemCode.OrderId.Value && x.Status == 1)
                    .ExecuteCommandAsync(cancellationToken);
            }

            await db.Ado.CommitTranAsync();
            return new RedeemPointCodeResponse
            {
                AddedPoints = awardedPoints,
                AvailablePoints = balanceAfter,
                RedeemedAt = now
            };
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<IssuedPointRedeemCodesResponse> IssueCodesAsync(
        IssuePointRedeemCodesRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsSuperAdmin)
        {
            throw new AppException(ErrorCodes.Forbidden, "Only super administrators can issue redeem codes");
        }
        if (request.Count is < 1 or > 100)
        {
            throw new AppException(ErrorCodes.BadRequest, "Count must be between 1 and 100");
        }
        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTime.Now)
        {
            throw new AppException(ErrorCodes.BadRequest, "Expiration time must be in the future");
        }

        var package = await GetEnabledPackageAsync(NormalizePackageCode(request.PackageCode), cancellationToken);
        PointRechargeOrderEntity? order = null;
        var now = DateTime.Now;

        await db.Ado.BeginTranAsync();
        try
        {
            if (!string.IsNullOrWhiteSpace(request.OrderNo))
            {
                if (request.Count != 1)
                {
                    throw new AppException(ErrorCodes.BadRequest, "An order can only be fulfilled with one redeem code");
                }

                var orderNo = request.OrderNo.Trim();
                order = await db.Queryable<PointRechargeOrderEntity>()
                    .TranLock(DbLockType.Wait)
                    .FirstAsync(x => x.OrderNo == orderNo, cancellationToken);
                if (order is null || order.PackageId != package.Id || order.Status != 0 || order.ExpiresAt <= now)
                {
                    throw new AppException(ErrorCodes.BadRequest, "Recharge order is invalid or already fulfilled");
                }
            }

            var points = order?.Points ?? package.Points;
            var codes = Enumerable.Range(0, request.Count)
                .Select(_ => PointRedeemCodeSecurity.Generate())
                .ToArray();
            var entities = codes.Select(code => new PointRedeemCodeEntity
            {
                CodeHash = PointRedeemCodeSecurity.Hash(code),
                CodeMask = PointRedeemCodeSecurity.Mask(code),
                PackageId = package.Id,
                OrderId = order?.Id,
                Points = points,
                Status = 0,
                ExpiresAt = request.ExpiresAt,
                CreatedBy = GetCurrentUserId(),
                CreatedAt = now
            }).ToArray();

            await db.Insertable(entities).ExecuteCommandAsync(cancellationToken);
            if (order is not null)
            {
                var affected = await db.Updateable<PointRechargeOrderEntity>()
                    .SetColumns(x => new PointRechargeOrderEntity
                    {
                        Status = 1,
                        PaidAt = now,
                        UpdatedAt = now
                    })
                    .Where(x => x.Id == order.Id && x.Status == 0)
                    .ExecuteCommandAsync(cancellationToken);
                if (affected != 1)
                {
                    throw new ConflictException("Recharge order state changed unexpectedly");
                }
            }

            await db.Ado.CommitTranAsync();
            return new IssuedPointRedeemCodesResponse
            {
                PackageCode = package.PackageCode,
                Points = points,
                Codes = codes
            };
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task<PointRechargePackageEntity> GetEnabledPackageAsync(
        string packageCode,
        CancellationToken cancellationToken)
    {
        var package = await db.Queryable<PointRechargePackageEntity>()
            .FirstAsync(x => x.PackageCode == packageCode && x.Status == 1 && !x.IsDeleted, cancellationToken);
        return package ?? throw new NotFoundException("Recharge package does not exist");
    }

    private async Task<int> GetEffectivePointsAsync(
        PointRechargePackageEntity package,
        long userId,
        CancellationToken cancellationToken)
    {
        if (package.RepeatPoints is not > 0)
        {
            return package.Points;
        }

        var hasRedeemedPackage = await db.Queryable<PointRedeemCodeEntity>()
            .AnyAsync(x => x.PackageId == package.Id && x.RedeemedByUserId == userId && x.Status == 1, cancellationToken);
        return hasRedeemedPackage ? package.RepeatPoints.Value : package.Points;
    }

    private static RechargeOrderDto MapOrder(PointRechargeOrderEntity order) => new()
    {
        OrderNo = order.OrderNo,
        PackageCode = order.PackageCode,
        Points = order.Points,
        PriceAmount = order.PriceAmount,
        Currency = order.Currency,
        Status = order.Status,
        PurchaseUrl = order.PurchaseUrl,
        ExpiresAt = order.ExpiresAt,
        CreatedAt = order.CreatedAt
    };

    private static string NormalizePackageCode(string value)
    {
        var packageCode = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (packageCode.Length is < 2 or > 50)
        {
            throw new AppException(ErrorCodes.BadRequest, "Package code is required");
        }

        return packageCode;
    }

    private static IReadOnlyList<string> ParseBenefits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ExpandPurchaseUrl(string? value, string orderNo, string packageCode, long userId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var expanded = value.Trim()
            .Replace("{orderNo}", Uri.EscapeDataString(orderNo), StringComparison.Ordinal)
            .Replace("{packageCode}", Uri.EscapeDataString(packageCode), StringComparison.Ordinal)
            .Replace("{userId}", userId.ToString(), StringComparison.Ordinal);
        return Uri.TryCreate(expanded, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? uri.ToString()
                : null;
    }

    private long GetCurrentUserId() => currentUser.UserId
        ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");

    private static AppException InvalidRedeemCode() =>
        new(ErrorCodes.BadRequest, "兑换码无效、已使用或已过期");
}
