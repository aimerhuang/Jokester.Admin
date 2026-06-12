using jokester.admin.Application.DTOs.Sites;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface ISiteService
{
    Task<PagedResult<SiteDto>> GetPageAsync(SiteQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<SiteDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<long> CreateAsync(SaveSiteRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(long id, SaveSiteRequest request, CancellationToken cancellationToken);

    Task UpdateStatusAsync(long id, UpdateSiteStatusRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
