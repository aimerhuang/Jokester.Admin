using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Community;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Mapster;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class CommunitySettingService(ISqlSugarClient db, ICurrentUser currentUser, SiteNoticeCache cache) : ICommunitySettingService
{
    public Task<QqGroupConfig> GetAsync(CancellationToken ct) => cache.GetCommunityAsync(async () =>
    {
        var row = await SettingAsync(false, ct);
        return row.Enabled ? row.Adapt<QqGroupConfig>() : new QqGroupConfig(false);
    }, ct);

    public async Task<AdminQqGroupConfig> GetAdminAsync(CancellationToken ct)
    {
        SiteNoticeAdmin.RequireAdmin(currentUser);
        var row = await SettingAsync(false, ct);
        var name = row.UpdatedBy is long id
            ? await db.Queryable<SysUserEntity>().Where(x => x.Id == id).Select(x => x.UserName).FirstAsync(ct)
            : null;
        return AdminConfig(row, name);
    }

    public async Task<AdminQqGroupConfig> SaveAsync(SaveQqGroupConfig request, CancellationToken ct)
    {
        var id = SiteNoticeAdmin.RequireAdmin(currentUser);
        await db.Ado.BeginTranAsync();
        AdminQqGroupConfig result;
        try
        {
            await SiteNoticeAdmin.CheckAdminAsync(db, id, true, ct);
            var row = await SettingAsync(true, ct);
            if (request.Revision is null || request.Enabled is null || request.RemindIntervalDays is null)
                throw new AppException(ErrorCodes.BadRequest, "请填写版本、开关和再次提醒间隔");
            if (row.Revision != request.Revision)
                throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.CommunitySettingRevisionConflict, "群配置已修改，请刷新后重试");

            row.Enabled = request.Enabled.Value;
            row.GroupName = row.Enabled ? SiteNoticeRules.RequiredText(request.GroupName, 50, "群名")
                : SiteNoticeRules.OptionalText(request.GroupName, 50, "群名") ?? string.Empty;
            row.GroupNumber = SiteNoticeRules.GroupNumber(request.GroupNumber, row.Enabled);
            row.JoinUrl = SiteNoticeRules.JoinUrl(request.JoinUrl);
            row.Title = SiteNoticeRules.RequiredText(request.Title, 40, "弹窗标题");
            row.Description = SiteNoticeRules.RequiredText(request.Description, 200, "弹窗说明");
            row.RemindIntervalDays = SiteNoticeRules.RemindInterval(request.RemindIntervalDays.Value);
            if (request.BumpPromptVersion) row.PromptVersion = checked(row.PromptVersion + 1);
            row.Revision = checked(row.Revision + 1);
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = id;
            await db.Updateable(row).ExecuteCommandAsync(ct);
            var name = await db.Queryable<SysUserEntity>().Where(x => x.Id == id).Select(x => x.UserName).FirstAsync(ct);
            result = AdminConfig(row, name);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
        cache.Invalidate();
        return result;
    }

    private async Task<SiteCommunitySettingEntity> SettingAsync(bool forUpdate, CancellationToken ct)
    {
        var query = db.Queryable<SiteCommunitySettingEntity>();
        if (forUpdate) query = query.TranLock(DbLockType.Wait);
        return await query.FirstAsync(x => x.Id == 1, ct) ?? throw new AppException(ErrorCodes.ServiceUnavailable, "请先执行公告与社群配置迁移");
    }

    private static AdminQqGroupConfig AdminConfig(SiteCommunitySettingEntity row, string? operatorName) =>
        new(row.Enabled, row.GroupName, row.GroupNumber, row.JoinUrl, row.Title, row.Description, row.RemindIntervalDays,
            row.PromptVersion, row.Revision, row.UpdatedBy, operatorName, row.UpdatedAt is DateTime date ? ApiDateTime.FromUtcStorage(date) : null);
}
