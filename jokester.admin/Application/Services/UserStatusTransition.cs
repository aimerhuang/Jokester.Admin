using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

/// <summary>Shared by all administrative edits to existing user status. Both methods run in the caller's transaction.</summary>
public sealed class UserStatusTransition(ISqlSugarClient db, IHttpContextAccessor http)
{
    internal async Task<SysUserEntity> LockTargetAsync(long operatorId, long id, CancellationToken ct)
    {
        var actor = await db.Queryable<SysUserEntity>().TranLock(DbLockType.Wait).FirstAsync(x => x.Id == operatorId, ct);
        if (actor is null || actor.IsDeleted || !actor.IsSuperAdmin || actor.Status != 1)
            throw new AppException(ErrorCodes.Forbidden, "仅启用的超级管理员可管理用户");
        return await db.Queryable<SysUserEntity>().TranLock(DbLockType.Wait).FirstAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new NotFoundException("用户不存在");
    }

    /// <summary>Other super admins cannot be demoted here; otherwise demote-then-disable bypasses the disable protection.</summary>
    internal static void EnsureSuperAdminFlagChangeAllowed(long operatorId, SysUserEntity user, bool isSuperAdmin)
    {
        if (user.IsSuperAdmin && !isSuperAdmin && user.Id != operatorId)
            throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.UserStatusProtected, "无法取消其他超级管理员的超管标记");
    }

    internal async Task RecordAsync(long operatorId, SysUserEntity user, int status, string? reason, CancellationToken ct)
    {
        var id = user.Id;
        if (status is not (0 or 1)) throw new AppException(ErrorCodes.BadRequest, "用户状态无效");
        if (status == 0 && (id == operatorId || user.IsSuperAdmin))
            throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.UserStatusProtected, "无法禁用自己或超级管理员");
        if (status == 1 && await db.Queryable<AccountDeletionRequestEntity>().AnyAsync(x => x.UserId == id &&
            (x.Status == "scheduled" || x.Status == "processing" || x.Status == "notification_pending" || x.Status == "failed"), ct))
            throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.UserDeletionInProgress, "用户正在注销");
        await db.Insertable(new SysUserStatusAuditEntity
        {
            UserId = id,
            OperatorUserId = operatorId,
            OperatorIp = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Action = status == 0 ? "disable" : "enable",
            Reason = string.IsNullOrEmpty(reason) ? null : reason,
            CreatedAt = DateTime.UtcNow
        }).ExecuteCommandAsync(ct);
    }
}
