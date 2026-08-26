using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

internal sealed class MembershipEntitlementLedger(ISqlSugarClient db)
{
    internal const string MonthlyVipTierCode = "monthly_vip";
    private const string ActiveStatus = "active";
    private const string RevokedStatus = "revoked";

    public async Task GrantMonthlyVipAsync(
        long userId,
        string source,
        string businessKey,
        DateTime startsAt,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        if (userId <= 0
            || string.IsNullOrWhiteSpace(source)
            || string.IsNullOrWhiteSpace(businessKey)
            || expiresAt <= startsAt)
        {
            throw new AppException(ErrorCodes.ServerError, "会员权益数据无效");
        }

        var existing = await db.Queryable<SysUserMembershipEntitlementEntity>()
            .FirstAsync(x => x.BusinessKey == businessKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != userId
                || existing.TierCode != MonthlyVipTierCode
                || existing.Source != source)
            {
                throw new AppException(ErrorCodes.ServerError, "会员权益业务键发生冲突");
            }

            return;
        }

        await db.Insertable(new SysUserMembershipEntitlementEntity
        {
            UserId = userId,
            TierCode = MonthlyVipTierCode,
            Source = source,
            BusinessKey = businessKey,
            StartsAt = startsAt,
            ExpiresAt = expiresAt,
            Status = ActiveStatus,
            CreatedAt = startsAt
        }).ExecuteCommandAsync(cancellationToken);
    }

    public async Task RevokeAsync(
        long userId,
        string businessKey,
        DateTime revokedAt,
        CancellationToken cancellationToken)
    {
        var existing = await db.Queryable<SysUserMembershipEntitlementEntity>()
            .FirstAsync(x => x.BusinessKey == businessKey, cancellationToken);
        if (existing is null)
        {
            return;
        }
        if (existing.UserId != userId)
        {
            throw new AppException(ErrorCodes.ServerError, "会员权益归属不一致");
        }
        if (existing.Status == RevokedStatus)
        {
            return;
        }

        var affected = await db.Updateable<SysUserMembershipEntitlementEntity>()
            .SetColumns(x => new SysUserMembershipEntitlementEntity
            {
                Status = RevokedStatus,
                RevokedAt = revokedAt,
                UpdatedAt = revokedAt
            })
            .Where(x => x.Id == existing.Id && x.Status == ActiveStatus)
            .ExecuteCommandAsync(cancellationToken);
        if (affected != 1)
        {
            throw new AppException(ErrorCodes.ServerError, "会员权益状态发生变化，请重试");
        }
    }

    public async Task<UserMembershipDto?> GetActiveAsync(
        long userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var entitlement = await db.Queryable<SysUserMembershipEntitlementEntity>()
            .Where(x => x.UserId == userId
                && x.TierCode == MonthlyVipTierCode
                && x.Status == ActiveStatus
                && x.RevokedAt == null
                && x.StartsAt <= now
                && x.ExpiresAt > now)
            .OrderBy(x => x.ExpiresAt, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        if (entitlement is null)
        {
            return null;
        }

        return new UserMembershipDto
        {
            TierCode = entitlement.TierCode,
            Status = ActiveStatus,
            ExpiresAt = ApiDateTime.FromLocalStorage(entitlement.ExpiresAt)
        };
    }
}
