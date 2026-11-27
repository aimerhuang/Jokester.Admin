using jokester.admin.Application.DTOs.Announcements;

namespace jokester.admin.Application.Abstractions;

public interface IAnnouncementService
{
    Task<AnnouncementFeed> ListAsync(int pageIndex, int pageSize, CancellationToken ct);
    Task<AnnouncementDetail> DetailAsync(long id, CancellationToken ct);
}
