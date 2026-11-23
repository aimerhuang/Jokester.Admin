using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.DTOs.Users;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface IUserAdminService
{
    Task<PagedResult<AdminUserListItem>> ListAsync(AdminUserQuery query, CancellationToken ct);
    Task<AdminUserDetail> DetailAsync(long id, CancellationToken ct);
    Task<AdminPointSummary> SummaryAsync(long id, AdminPointRange query, CancellationToken ct);
    Task<PagedResult<PointDetailDto>> DetailsAsync(long id, AdminPointQuery query, CancellationToken ct);
    Task<AdminStatusResult> ChangeStatusAsync(long id, AdminStatusRequest request, CancellationToken ct, bool legacy = false);
    Task<PagedResult<AdminStatusAudit>> AuditsAsync(long id, AdminPageQuery query, CancellationToken ct);
}
