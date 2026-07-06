using jokester.admin.Application.DTOs.Common;

namespace jokester.admin.Application.DTOs.Blog;

/// <summary>
/// 博客文章分页查询参数。
/// </summary>
public sealed class BlogArticleQuery : PageQuery
{
    /// <summary>
    /// 文章状态：0=草稿，1=已发布，2=隐藏。
    /// </summary>
    public int? Status { get; init; }

    /// <summary>
    /// 关键词，匹配标题或摘要。
    /// </summary>
    public string? Keyword { get; init; }
}

/// <summary>
/// 博客文章信息。
/// </summary>
public sealed class BlogArticleDto
{
    /// <summary>
    /// 文章 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 站点 ID。
    /// </summary>
    public long SiteId { get; init; }

    /// <summary>
    /// 分类 ID。
    /// </summary>
    public long? CategoryId { get; init; }

    /// <summary>
    /// 分类名称。
    /// </summary>
    public string? CategoryName { get; set; }

    /// <summary>
    /// 文章标题。
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// 文章摘要。
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// 文章正文。
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 封面图地址。
    /// </summary>
    public string? CoverUrl { get; init; }

    /// <summary>
    /// 缩略图地址。
    /// </summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// 文章状态：0=草稿，1=已发布，2=隐藏。
    /// </summary>
    public int Status { get; init; }
}

/// <summary>
/// 保存博客文章请求。
/// </summary>
public sealed class SaveBlogArticleRequest
{
    /// <summary>
    /// 文章标题。
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// 文章摘要。
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// 文章正文。
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 封面图地址。
    /// </summary>
    public string? CoverUrl { get; init; }

    /// <summary>
    /// 分类 ID。
    /// </summary>
    public long? CategoryId { get; init; }

    /// <summary>
    /// 标签，多个标签用逗号分隔。
    /// </summary>
    public string? Tags { get; init; }

    /// <summary>
    /// 文章状态：0=草稿，1=已发布，2=隐藏。
    /// </summary>
    public int? Status { get; init; }
}
