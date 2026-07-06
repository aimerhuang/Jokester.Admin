using jokester.admin.Application.DTOs.Blog;

namespace jokester.admin.Application.Abstractions;

public interface IBlogCategoryService
{
    Task<IReadOnlyCollection<BlogCategoryDto>> GetListAsync(CancellationToken cancellationToken);

    Task<long> CreateAsync(SaveBlogCategoryRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(long id, SaveBlogCategoryRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
