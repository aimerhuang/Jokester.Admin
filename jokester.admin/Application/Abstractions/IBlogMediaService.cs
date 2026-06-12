using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Common;
using Microsoft.AspNetCore.Http;

namespace jokester.admin.Application.Abstractions;

public interface IBlogMediaService
{
    Task<UploadMediaResponse> UploadAsync(IFormFile file, long uploaderId, CancellationToken cancellationToken);

    Task<PagedResult<BlogMediaDto>> GetPageAsync(BlogMediaQuery query, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
