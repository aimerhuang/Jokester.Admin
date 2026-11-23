using System.Globalization;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.DTOs.Users;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Mapster;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class UserAdminService(ISqlSugarClient db, ICurrentUser currentUser, IPointService points,
    IPermissionCacheInvalidator cache, IRefreshTokenStore sessions, UserStatusTransition transitions) : IUserAdminService
{
    private long RequireAdmin()
    {
        var id = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "请先登录");
        if (!currentUser.IsSuperAdmin) throw Forbidden();
        return id;
    }

    private static AppException Forbidden() => new(ErrorCodes.Forbidden, "仅超级管理员可管理用户");
    private static NotFoundException Missing() => new("用户不存在");

    private async Task RequireTargetAsync(long id, CancellationToken ct)
    {
        RequireAdmin();
        if (!await db.Queryable<SysUserEntity>().AnyAsync(x => x.Id == id && !x.IsDeleted, ct)) throw Missing();
    }

    public async Task<PagedResult<AdminUserListItem>> ListAsync(AdminUserQuery query, CancellationToken ct)
    {
        RequireAdmin();
        if (query.Status is not (null or 0 or 1) || query.Keyword?.Length > 100 || query.SortOrder is not ("desc" or "asc"))
            throw new AppException(ErrorCodes.BadRequest, "用户筛选条件无效");
        var users = db.Queryable<SysUserEntity>().Where(x => !x.IsDeleted)
            .WhereIF(query.Status.HasValue, x => x.Status == query.Status)
            .WhereIF(query.IsSuperAdmin.HasValue, x => x.IsSuperAdmin == query.IsSuperAdmin);
        var keyword = query.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            // INSTR treats %, _ and backslashes literally in both MySQL and SQLite.
            var numeric = long.TryParse(keyword, NumberStyles.None, CultureInfo.InvariantCulture, out var id);
            const string match = "instr(user_name, @keyword) > 0 OR instr(nick_name, @keyword) > 0 OR instr(email, @keyword) > 0";
            users = numeric ? users.Where($"({match} OR id = @id)", new { keyword, id })
                : users.Where($"({match})", new { keyword });
        }
        var order = query.SortOrder == "asc" ? OrderByType.Asc : OrderByType.Desc;
        users = query.SortBy switch
        {
            "createdAt" => users.OrderBy(x => x.CreatedAt, order),
            "lastLoginTime" => users.OrderBy(x => x.LastLoginTime, order),
            "pointBalance" => users.OrderBy(x => x.PointBalance, order),
            _ => throw new AppException(ErrorCodes.BadRequest, "排序字段无效")
        };
        RefAsync<int> total = 0;
        var rows = await users.OrderByDescending(x => x.Id).ToPageListAsync(query.PageIndex, query.PageSize, total);
        var memberships = await MembershipsAsync(rows.Select(x => x.Id).ToArray(), ct);
        return Page(query, total, rows.Select(x => Profile<AdminUserListItem>(x, memberships)).ToArray());
    }

    private async Task<Dictionary<long, AdminMembership>> MembershipsAsync(long[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var now = DateTime.Now;
        var rows = await db.Queryable<SysUserMembershipEntitlementEntity>()
            .Where(x => ids.Contains(x.UserId) && x.TierCode == MembershipEntitlementLedger.MonthlyVipTierCode
                && x.Status == "active" && x.RevokedAt == null && x.StartsAt <= now && x.ExpiresAt > now)
            .GroupBy(x => x.UserId).Select(x => new { x.UserId, ExpiresAt = SqlFunc.AggregateMax(x.ExpiresAt) }).ToListAsync(ct);
        return rows.ToDictionary(x => x.UserId, x => new AdminMembership(MembershipEntitlementLedger.MonthlyVipTierCode, ApiDateTime.FromLocalStorage(x.ExpiresAt)));
    }

    private static T Profile<T>(SysUserEntity user, Dictionary<long, AdminMembership> memberships) where T : AdminUserProfile
    {
        var dto = user.Adapt<T>();
        dto.CreatedAt = ApiDateTime.FromLocalStorage(user.CreatedAt);
        dto.LastLoginTime = user.LastLoginTime is {} last ? ApiDateTime.FromLocalStorage(last) : null;
        dto.Membership = memberships.GetValueOrDefault(user.Id);
        return dto;
    }

    public async Task<AdminUserDetail> DetailAsync(long id, CancellationToken ct)
    {
        RequireAdmin();
        var user = await db.Queryable<SysUserEntity>().FirstAsync(x => x.Id == id && !x.IsDeleted, ct) ?? throw Missing();
        var dto = Profile<AdminUserDetail>(user, await MembershipsAsync([id], ct));
        var balance = await points.GetAdminBalanceAsync(id, ct);
        dto.Points = new(balance.AvailablePoints, balance.PermanentPoints, balance.ExpiringPoints, balance.NextExpiringPoints, balance.NextExpireAt);
        return dto;
    }

    private ISugarQueryable<UserPointDetailEntity> Ledger(long id, AdminPointRange range)
    {
        if (range.From > range.To) throw new AppException(ErrorCodes.BadRequest, "开始时间晚于结束时间");
        var rows = db.Queryable<UserPointDetailEntity>().Where(x => x.UserId == id);
        if (range.From is {} start)
        {
            var from = ApiDateTime.ToLocalStorage(start.UtcDateTime);
            rows = rows.Where(x => x.CreatedAt >= from);
        }
        if (range.To is {} end)
        {
            var to = ApiDateTime.ToLocalStorage(end.UtcDateTime);
            rows = rows.Where(x => x.CreatedAt < to);
        }
        return rows;
    }

    public async Task<AdminPointSummary> SummaryAsync(long id, AdminPointRange query, CancellationToken ct)
    {
        await RequireTargetAsync(id, ct);
        var groups = await Ledger(id, query).GroupBy(x => x.Source).Select(x => new {
            x.Source, Count = SqlFunc.AggregateCount(x.Id), TotalPoints = SqlFunc.AggregateSum((long)x.ChangePoints)
        }).ToListAsync(ct);
        var sources = groups.Select(x => new AdminSourceTotal(x.Source, x.Count, x.TotalPoints))
            .OrderByDescending(x => Math.Abs(x.TotalPoints)).ThenBy(x => x.Source, StringComparer.Ordinal).ToArray();
        long Sum(params string[] names) => sources.Where(x => names.Contains(x.Source)).Sum(x => x.TotalPoints);
        return new(query.From?.UtcDateTime, query.To?.UtcDateTime, Sum("register", "sign_in", "recharge", "apple_iap"),
            -Sum("image_generate", "image_refund"), -Sum("sign_in_expire", "recharge_expire", "apple_iap_expire", "point_expire"),
            -Sum("apple_refund"), sources.Where(x => x.Source == "image_generate").Sum(x => x.Count), sources);
    }

    public async Task<PagedResult<PointDetailDto>> DetailsAsync(long id, AdminPointQuery query, CancellationToken ct)
    {
        await RequireTargetAsync(id, ct);
        var rows = Ledger(id, query).WhereIF(query.Source.Length > 0, x => query.Source.Contains(x.Source));
        rows = query.Direction switch {
            null => rows, "in" => rows.Where(x => x.ChangePoints > 0), "out" => rows.Where(x => x.ChangePoints < 0),
            _ => throw new AppException(ErrorCodes.BadRequest, "积分方向无效")
        };
        RefAsync<int> total = 0;
        var items = await rows.OrderByDescending(x => x.CreatedAt).OrderByDescending(x => x.Id)
            .ToPageListAsync(query.PageIndex, query.PageSize, total);
        return Page(query, total, items.Select(x => new PointDetailDto { Id = x.Id, ChangePoints = x.ChangePoints,
            BalanceAfter = x.BalanceAfter, Source = x.Source, ChangeType = x.ChangeType, Remark = x.Remark,
            CreatedAt = ApiDateTime.FromLocalStorage(x.CreatedAt) }).ToArray());
    }

    public async Task<PagedResult<AdminStatusAudit>> AuditsAsync(long id, AdminPageQuery query, CancellationToken ct)
    {
        await RequireTargetAsync(id, ct);
        RefAsync<int> total = 0;
        var rows = await db.Queryable<SysUserStatusAuditEntity>().Where(x => x.UserId == id)
            .OrderByDescending(x => x.CreatedAt).OrderByDescending(x => x.Id).ToPageListAsync(query.PageIndex, query.PageSize, total);
        var ids = rows.Select(x => x.OperatorUserId).Distinct().ToArray();
        var operators = ids.Length == 0 ? [] : await db.Queryable<SysUserEntity>().Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.UserName }).ToListAsync(ct);
        var names = operators.ToDictionary(x => x.Id, x => x.UserName);
        return Page(query, total, rows.Select(x => new AdminStatusAudit(x.Id, x.Action, x.Reason, x.OperatorUserId,
            names.GetValueOrDefault(x.OperatorUserId), ApiDateTime.FromUtcStorage(x.CreatedAt))).ToArray());
    }

    private static PagedResult<T> Page<T>(AdminPageQuery query, int total, T[] items) =>
        new() { PageIndex = query.PageIndex, PageSize = query.PageSize, Total = total, Items = items };

    public async Task<AdminStatusResult> ChangeStatusAsync(long id, AdminStatusRequest request, CancellationToken ct, bool legacy = false)
    {
        var operatorId = RequireAdmin();
        var reason = request.Reason?.Trim();
        var status = request.Status;
        if (status is not (0 or 1) || reason?.Length > 200)
            throw new AppException(ErrorCodes.BadRequest, "状态无效或原因不符合要求");
        bool changed;
        await db.Ado.BeginTranAsync();
        try
        {
            var user = await transitions.LockTargetAsync(operatorId, id, ct);
            changed = user.Status != status;
            if (changed)
            {
                // Checked after the same-state short-circuit: repeating a disable succeeds without a new reason.
                if (status == 0 && !legacy && string.IsNullOrEmpty(reason))
                    throw new AppException(ErrorCodes.BadRequest, "禁用必须填写原因");
                await transitions.RecordAsync(operatorId, user, status, reason, ct);
                await db.Updateable<SysUserEntity>().SetColumns(x => new SysUserEntity { Status = status, UpdatedAt = DateTime.Now })
                    .Where(x => x.Id == id).ExecuteCommandAsync(ct);
            }
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
        if (changed) await Task.WhenAll(cache.RemoveUserAsync(id, CancellationToken.None), sessions.RevokeUserSessionsAsync(id, CancellationToken.None));
        return new(id, status);
    }
}
