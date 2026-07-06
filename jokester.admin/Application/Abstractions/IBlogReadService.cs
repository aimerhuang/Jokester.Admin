using jokester.admin.Application.DTOs.Blog;

namespace jokester.admin.Application.Abstractions;

public interface IBlogReadService
{
    Task<BlogSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BlogLatestTitleDto>> GetLatestTitlesAsync(int take, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BlogLatestCommentDto>> GetLatestCommentsAsync(int take, CancellationToken cancellationToken);

    Task<BlogSiteInfoDto> GetSiteInfoAsync(CancellationToken cancellationToken);
}
