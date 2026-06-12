using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface IBlogCommentService
{
    Task<PagedResult<BlogCommentDto>> GetPageAsync(BlogCommentQuery query, CancellationToken cancellationToken);

    Task<PagedResult<PublicBlogCommentDto>> GetPublicPageAsync(PublicBlogCommentQuery query, CancellationToken cancellationToken);

    Task<CreateBlogCommentResult> CreateAsync(
        CreateBlogCommentRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);

    Task ReviewAsync(long id, ReviewBlogCommentRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
