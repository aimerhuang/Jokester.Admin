using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface IBlogArticleService
{
    Task<PagedResult<BlogArticleDto>> GetPageAsync(BlogArticleQuery query, CancellationToken cancellationToken);

    Task<BlogArticleDto?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<long> CreateAsync(SaveBlogArticleRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(long id, SaveBlogArticleRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);

    Task PublishAsync(long id, CancellationToken cancellationToken);
}
