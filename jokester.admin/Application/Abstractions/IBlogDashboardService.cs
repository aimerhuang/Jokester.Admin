using jokester.admin.Application.DTOs.Blog;

namespace jokester.admin.Application.Abstractions;

public interface IBlogDashboardService
{
    Task<BlogDashboardStatsDto> GetStatsAsync(CancellationToken cancellationToken);
}
