using jokester.admin.Application.DTOs.Announcements;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface IAnnouncementAdminService
{
    Task<PagedResult<AdminAnnouncementListItem>> ListAsync(AdminAnnouncementQuery query, CancellationToken ct);
    Task<AdminAnnouncementDetail> DetailAsync(long id, CancellationToken ct);
    Task<AdminAnnouncementDetail> CreateAsync(CreateAnnouncementRequest request, CancellationToken ct);
    Task<AdminAnnouncementDetail> UpdateAsync(long id, UpdateAnnouncementRequest request, CancellationToken ct);
    Task<AdminAnnouncementDetail> ChangeStatusAsync(long id, AnnouncementStatusRequest request, CancellationToken ct);
    Task DeleteAsync(long id, int? revision, CancellationToken ct);
    Task<AnnouncementCoverUpload> UploadCoverAsync(IFormFile file, CancellationToken ct);
}
