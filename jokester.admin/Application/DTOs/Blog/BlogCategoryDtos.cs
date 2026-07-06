namespace jokester.admin.Application.DTOs.Blog;

/// <summary>
/// 博客分类信息。
/// </summary>
public sealed class BlogCategoryDto
{
    public long Id { get; init; }

    public long SiteId { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Sort { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// 保存博客分类请求。
/// </summary>
public sealed class SaveBlogCategoryRequest
{
    public string Name { get; init; } = string.Empty;

    public int Sort { get; init; }
}
