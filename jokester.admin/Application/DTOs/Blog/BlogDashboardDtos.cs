namespace jokester.admin.Application.DTOs.Blog;

/// <summary>
/// 博客仪表盘统计。
/// </summary>
public sealed class BlogDashboardStatsDto
{
    /// <summary>
    /// 文章统计。
    /// </summary>
    public BlogArticleStatsDto Articles { get; init; } = new();

    /// <summary>
    /// 评论统计。
    /// </summary>
    public BlogCommentStatsDto Comments { get; init; } = new();

    /// <summary>
    /// 媒体统计。
    /// </summary>
    public BlogMediaStatsDto Media { get; init; } = new();

    /// <summary>
    /// 最近待审核评论列表。
    /// </summary>
    public IReadOnlyCollection<BlogCommentDto> RecentPendingComments { get; init; } = [];
}

/// <summary>
/// 博客文章统计。
/// </summary>
public sealed class BlogArticleStatsDto
{
    /// <summary>
    /// 文章总数。
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// 草稿数量。
    /// </summary>
    public int Draft { get; init; }

    /// <summary>
    /// 已发布数量。
    /// </summary>
    public int Published { get; init; }

    /// <summary>
    /// 隐藏数量。
    /// </summary>
    public int Hidden { get; init; }

    /// <summary>
    /// 浏览量总数。
    /// </summary>
    public int ViewCount { get; init; }
}

/// <summary>
/// 博客评论统计。
/// </summary>
public sealed class BlogCommentStatsDto
{
    /// <summary>
    /// 评论总数。
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// 待审核数量。
    /// </summary>
    public int Pending { get; init; }

    /// <summary>
    /// 已通过数量。
    /// </summary>
    public int Approved { get; init; }

    /// <summary>
    /// 已拒绝数量。
    /// </summary>
    public int Rejected { get; init; }

    /// <summary>
    /// 垃圾评论数量。
    /// </summary>
    public int Spam { get; init; }

    /// <summary>
    /// 今日新增评论数量。
    /// </summary>
    public int Today { get; init; }
}

/// <summary>
/// 博客媒体统计。
/// </summary>
public sealed class BlogMediaStatsDto
{
    /// <summary>
    /// 媒体总数。
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// 文件总大小，单位字节。
    /// </summary>
    public long TotalBytes { get; init; }
}
