using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Announcements;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Mapster;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AnnouncementAdminService(ISqlSugarClient db, ICurrentUser currentUser,
    IWebHostEnvironment environment, SiteNoticeCache cache) : IAnnouncementAdminService
{
    private static NotFoundException Missing() => new("公告不存在");
    private string WebRoot => environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");

    public async Task<PagedResult<AdminAnnouncementListItem>> ListAsync(AdminAnnouncementQuery query, CancellationToken ct)
    {
        SiteNoticeAdmin.RequireAdmin(currentUser);
        if (query.PageIndex < 1 || query.PageSize is < 1 or > 100 || query.Status is not (null or 0 or 1 or 2))
            throw new AppException(ErrorCodes.BadRequest, "公告筛选条件无效");
        var keyword = SiteNoticeRules.OptionalText(query.Keyword, 80, "关键字");
        var announcements = db.Queryable<SiteAnnouncementEntity>().Where(x => !x.IsDeleted)
            .WhereIF(query.Status.HasValue, x => x.Status == query.Status);
        if (keyword != null) announcements = announcements.Where("instr(title, @keyword) > 0", new { keyword });
        var total = await announcements.CountAsync(ct);
        var offset = (long)(query.PageIndex - 1) * query.PageSize;
        var page = offset >= total ? [] : await announcements.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .OrderByDescending(x => x.Id).Skip((int)offset).Take(query.PageSize).ToListAsync(ct);
        var names = await GetOperatorNamesAsync(page, ct);
        return new() { Total = total, PageIndex = query.PageIndex, PageSize = query.PageSize,
            Items = page.Select(row => ToAdminItem<AdminAnnouncementListItem>(row, names)).ToArray() };
    }

    public async Task<AdminAnnouncementDetail> DetailAsync(long id, CancellationToken ct)
    {
        SiteNoticeAdmin.RequireAdmin(currentUser);
        var row = await db.Queryable<SiteAnnouncementEntity>().FirstAsync(x => x.Id == id && !x.IsDeleted, ct) ?? throw Missing();
        return await ToDetailAsync(row, ct);
    }

    private async Task<Dictionary<long, string>> GetOperatorNamesAsync(IReadOnlyList<SiteAnnouncementEntity> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var ids = rows.SelectMany(row => new[] { row.CreatedBy, row.UpdatedBy ?? row.CreatedBy }).Distinct().ToArray();
        var operators = await db.Queryable<SysUserEntity>().Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.UserName }).ToListAsync(ct);
        return operators.ToDictionary(x => x.Id, x => x.UserName);
    }

    private static T ToAdminItem<T>(SiteAnnouncementEntity row, Dictionary<long, string> names) where T : AdminAnnouncementListItem
    {
        var item = row.Adapt<T>();
        item.HasDetail = row.Content != null || row.LinkUrl != null;
        if (row.PublishedAt is {} published) item.PublishedAt = ApiDateTime.FromUtcStorage(published);
        item.CreatedAt = ApiDateTime.FromUtcStorage(row.CreatedAt);
        if (row.UpdatedAt is {} updated) item.UpdatedAt = ApiDateTime.FromUtcStorage(updated);
        item.CreatedByName = names.GetValueOrDefault(row.CreatedBy);
        if (row.UpdatedBy is {} operatorId) item.UpdatedByName = names.GetValueOrDefault(operatorId);
        return item;
    }

    private async Task<AdminAnnouncementDetail> ToDetailAsync(SiteAnnouncementEntity row, CancellationToken ct) =>
        ToAdminItem<AdminAnnouncementDetail>(row, await GetOperatorNamesAsync([row], ct));

    public Task<AdminAnnouncementDetail> CreateAsync(CreateAnnouncementRequest request, CancellationToken ct) =>
        WriteAsync(null, null, announcement =>
        {
            if (request.Status is not (0 or 1)) throw new AppException(ErrorCodes.BadRequest, "新公告请选择草稿或发布");
            ApplyContent(announcement, request);
            announcement.Status = request.Status.Value;
            return true;
        }, ct);

    public Task<AdminAnnouncementDetail> UpdateAsync(long id, UpdateAnnouncementRequest request, CancellationToken ct) =>
        WriteAsync(id, request.Revision, announcement =>
        {
            ApplyContent(announcement, request);
            return true;
        }, ct);

    public Task<AdminAnnouncementDetail> ChangeStatusAsync(long id, AnnouncementStatusRequest request, CancellationToken ct) =>
        WriteAsync(id, request.Revision, announcement =>
        {
            if (request.Status == announcement.Status) return false;
            if ((announcement.Status, request.Status) is not ((0, 1) or (1, 2) or (2, 1)))
                throw new AppException(ErrorCodes.BadRequest, "公告状态变更无效");
            announcement.Status = request.Status.Value;
            return true;
        }, ct);

    public Task DeleteAsync(long id, int? revision, CancellationToken ct) => WriteAsync(id, revision, row =>
    {
        if (row.Status == 1) throw new ConflictException("请先下线，再删除公告");
        row.IsDeleted = true;
        return true;
    }, ct);

    private void ApplyContent(SiteAnnouncementEntity row, AnnouncementContentRequest request)
    {
        row.Title = SiteNoticeRules.RequiredText(request.Title, 80, "标题");
        row.Summary = SiteNoticeRules.RequiredText(request.Summary, 200, "摘要");
        row.Content = SiteNoticeRules.Content(request.Content);
        row.Category = SiteNoticeRules.Category(request.Category);
        row.CoverUrl = SiteNoticeRules.CoverUrl(request.CoverUrl);
        if (row.CoverUrl != null && !File.Exists(Path.Combine(WebRoot, row.CoverUrl[1..])))
            throw new AppException(ErrorCodes.BadRequest, "封面不存在，请重新上传");
        row.VersionLabel = SiteNoticeRules.OptionalText(request.VersionLabel, 20, "版本号");
        row.LinkText = SiteNoticeRules.OptionalText(request.LinkText, 20, "按钮文案");
        row.LinkUrl = SiteNoticeRules.LinkUrl(request.LinkUrl);
        if ((row.LinkText == null) != (row.LinkUrl == null))
            throw new AppException(ErrorCodes.BadRequest, "请同时填写按钮文案与链接");
        row.IsPinned = request.IsPinned;
        row.IsImportant = request.IsImportant;
    }

    private async Task<AdminAnnouncementDetail> WriteAsync(long? id, int? revision, Func<SiteAnnouncementEntity, bool> change, CancellationToken ct)
    {
        var operatorId = SiteNoticeAdmin.RequireAdmin(currentUser);
        AdminAnnouncementDetail result;
        bool changed;
        await db.Ado.BeginTranAsync();
        try
        {
            await SiteNoticeAdmin.CheckAdminAsync(db, operatorId, true, ct);
            SiteAnnouncementEntity row;
            if (id.HasValue)
            {
                row = await db.Queryable<SiteAnnouncementEntity>().TranLock(DbLockType.Wait)
                    .FirstAsync(x => x.Id == id && !x.IsDeleted, ct) ?? throw Missing();
                if (!revision.HasValue) throw new AppException(ErrorCodes.BadRequest, "请提供公告 revision");
                if (row.Revision != revision.Value) throw new AppException(ErrorCodes.Conflict,
                    MachineErrorCodes.AnnouncementRevisionConflict, "公告已被其他人修改，请刷新后重试");
            }
            else row = new() { CreatedBy = operatorId };

            var wasPublishedPin = row.Status == 1 && row.IsPinned;
            changed = change(row);
            if (changed)
            {
                if (!wasPublishedPin && row.Status == 1 && row.IsPinned) await EnsurePinAvailableAsync(ct);
                var now = DateTime.UtcNow;
                if (row.Status == 1) row.PublishedAt ??= now;
                row.UpdatedAt = now;
                row.UpdatedBy = operatorId;
                if (id.HasValue)
                {
                    row.Revision = checked(row.Revision + 1);
                    await db.Updateable(row).ExecuteCommandAsync(ct);
                }
                else
                {
                    row.CreatedAt = now;
                    row.Id = await db.Insertable(row).ExecuteReturnBigIdentityAsync();
                }
            }
            result = await ToDetailAsync(row, ct);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
        if (changed) cache.Invalidate();
        return result;
    }

    private async Task EnsurePinAvailableAsync(CancellationToken ct)
    {
        // Even when no pins exist, this row serializes contenders for the three slots.
        // All earlier transaction reads are locking reads: COUNT sees the last committed admission.
        _ = await db.Queryable<SiteCommunitySettingEntity>().TranLock(DbLockType.Wait).FirstAsync(x => x.Id == 1, ct)
            ?? throw new AppException(ErrorCodes.ServiceUnavailable, "请先执行公告与社群配置迁移");
        var count = await db.Queryable<SiteAnnouncementEntity>().CountAsync(x => x.Status == 1 && x.IsPinned && !x.IsDeleted, ct);
        if (count >= 3) throw new ConflictException("最多置顶 3 条已发布公告");
    }

    public async Task<AnnouncementCoverUpload> UploadCoverAsync(IFormFile file, CancellationToken ct)
    {
        var operatorId = SiteNoticeAdmin.RequireAdmin(currentUser);
        await SiteNoticeAdmin.CheckAdminAsync(db, operatorId, false, ct);
        var image = await ImageUploadValidator.ValidateAsync(file, 2 * 1024 * 1024, ct);
        if (image.MimeType is not ("image/png" or "image/jpeg" or "image/webp"))
            throw new AppException(ErrorCodes.BadRequest, "公告封面仅支持 PNG、JPEG 或 WebP");
        var key = $"site-media/announcement/{DateTime.UtcNow:yyyyMM}/{Guid.NewGuid():N}{image.Extension}";
        var path = Path.Combine(WebRoot, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, image.Content, ct);
        return new("/" + key);
    }
}
