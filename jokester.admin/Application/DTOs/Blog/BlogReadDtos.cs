namespace jokester.admin.Application.DTOs.Blog;

/// <summary>
/// 博客首页摘要统计。
/// </summary>
public sealed class BlogSummaryDto
{
    public int ArticleCount { get; init; }

    public int CommentCount { get; init; }

    public int ViewCount { get; init; }
}

/// <summary>
/// 最新博客标题。
/// </summary>
public sealed class BlogLatestTitleDto
{
    public long Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }

    public long? CategoryId { get; init; }

    public string? CategoryName { get; init; }
}

/// <summary>
/// 最新博客评论。
/// </summary>
public sealed class BlogLatestCommentDto
{
    public long Id { get; init; }

    public long ArticleId { get; init; }

    public string ArticleTitle { get; init; } = string.Empty;

    public string AuthorName { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// 博客网站信息。
/// </summary>
public sealed class BlogSiteInfoDto
{
    public DateTime BuildDate { get; init; }

    public string BuildDateText { get; init; } = string.Empty;

    public int RunningDays { get; init; }

    public int CommentCount { get; init; }

    public int ArticleCount { get; init; }

    public int ViewCount { get; init; }
}
