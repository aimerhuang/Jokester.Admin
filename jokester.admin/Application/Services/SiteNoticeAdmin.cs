using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

internal static class SiteNoticeAdmin
{
    public const string Policy = "SiteNoticeAdmin";

    public static long RequireAdmin(ICurrentUser currentUser)
    {
        var id = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "请先登录");
        return currentUser.IsSuperAdmin ? id : throw Forbidden();
    }

    public static async Task CheckAdminAsync(ISqlSugarClient db, long id, bool lockRow, CancellationToken ct)
    {
        var users = db.Queryable<SysUserEntity>();
        if (lockRow) users = users.TranLock(DbLockType.Wait);
        if (await users.FirstAsync(x => x.Id == id, ct) is not { IsSuperAdmin: true, Status: 1, IsDeleted: false })
            throw Forbidden();
    }

    private static AppException Forbidden() => new(ErrorCodes.Forbidden, "仅启用的超级管理员可管理公告和 QQ 群配置");
}
