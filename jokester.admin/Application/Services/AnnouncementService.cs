using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Announcements;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Mapster;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AnnouncementService(ISqlSugarClient db, SiteNoticeCache cache) : IAnnouncementService
{
    public async Task<AnnouncementFeed> ListAsync(int pageIndex, int pageSize, CancellationToken ct)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 50)
            throw new AppException(ErrorCodes.BadRequest, "公告分页参数无效");
        var snapshot = await cache.GetAnnouncementsAsync(() => LoadSnapshotAsync(ct), ct);
        var offset = (long)(pageIndex - 1) * pageSize;
        var page = offset >= snapshot.Items.Count ? [] : snapshot.Items.Skip((int)offset).Take(pageSize).ToArray();
        return new(snapshot.Items.Count, pageIndex, pageSize, snapshot.LatestPublishedAt, page);
    }

    private async Task<AnnouncementSnapshot> LoadSnapshotAsync(CancellationToken ct)
    {
        var items = await db.Queryable<SiteAnnouncementEntity>().Where(x => x.Status == 1 && !x.IsDeleted)
            .OrderByDescending(x => x.IsPinned).OrderByDescending(x => x.PublishedAt).OrderByDescending(x => x.Id)
            .Select(x => new AnnouncementListItem
            {
                Id = x.Id, Title = x.Title, Summary = x.Summary, Category = x.Category, CoverUrl = x.CoverUrl,
                VersionLabel = x.VersionLabel, IsPinned = x.IsPinned, IsImportant = x.IsImportant,
                PublishedAt = x.PublishedAt, HasDetail = x.Content != null || x.LinkUrl != null
            }).ToListAsync(ct);
        foreach (var item in items)
            if (item.PublishedAt is {} time) item.PublishedAt = ApiDateTime.FromUtcStorage(time);
        return new(items, items.Max(x => x.PublishedAt));
    }

    public async Task<AnnouncementDetail> DetailAsync(long id, CancellationToken ct)
    {
        var row = await db.Queryable<SiteAnnouncementEntity>().FirstAsync(x => x.Id == id && x.Status == 1 && !x.IsDeleted, ct)
            ?? throw new NotFoundException("公告不存在或已下线");
        var detail = row.Adapt<AnnouncementDetail>();
        detail.HasDetail = row.Content != null || row.LinkUrl != null;
        if (row.PublishedAt is {} time) detail.PublishedAt = ApiDateTime.FromUtcStorage(time);
        return detail;
    }
}
