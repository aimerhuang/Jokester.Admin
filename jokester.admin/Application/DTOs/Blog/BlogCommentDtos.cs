using jokester.admin.Application.DTOs.Common;

namespace jokester.admin.Application.DTOs.Blog;

/// <summary>
/// 博客评论后台分页查询参数。
/// </summary>
public sealed class BlogCommentQuery : PageQuery
{
    /// <summary>
    /// 文章 ID。
    /// </summary>
    public long? ArticleId { get; init; }

    /// <summary>
    /// 父评论 ID，空表示一级评论。
    /// </summary>
    public long? ParentId { get; init; }

    /// <summary>
    /// 评论状态：0=待审核，1=已通过，2=已拒绝，3=垃圾评论。
    /// </summary>
    public int? Status { get; init; }

    /// <summary>
    /// 关键词，匹配评论内容或评论者信息。
    /// </summary>
    public string? Keyword { get; init; }
}

/// <summary>
/// 博客评论公开分页查询参数。
/// </summary>
public sealed class PublicBlogCommentQuery : PageQuery
{
    /// <summary>
    /// 文章 ID。
    /// </summary>
    public long ArticleId { get; init; }

    /// <summary>
    /// 父评论 ID，空表示一级评论。
    /// </summary>
    public long? ParentId { get; init; }
}

/// <summary>
/// 博客评论后台信息。
/// </summary>
public sealed class BlogCommentDto
{
    /// <summary>
    /// 评论 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 文章 ID。
    /// </summary>
    public long ArticleId { get; init; }

    /// <summary>
    /// 父评论 ID，空表示一级评论。
    /// </summary>
    public long? ParentId { get; init; }

    /// <summary>
    /// 评论者昵称。
    /// </summary>
    public string AuthorName { get; init; } = string.Empty;

    /// <summary>
    /// 评论者邮箱。
    /// </summary>
    public string? AuthorEmail { get; init; }

    /// <summary>
    /// 评论者网站。
    /// </summary>
    public string? AuthorWebsite { get; init; }

    /// <summary>
    /// 评论内容。
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 评论状态：0=待审核，1=已通过，2=已拒绝，3=垃圾评论。
    /// </summary>
    public int Status { get; init; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// 审核时间。
    /// </summary>
    public DateTime? ReviewedAt { get; init; }

    /// <summary>
    /// 审核人用户 ID。
    /// </summary>
    public long? ReviewedBy { get; init; }
}

/// <summary>
/// 博客评论公开信息。
/// </summary>
public sealed class PublicBlogCommentDto
{
    /// <summary>
    /// 评论 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 文章 ID。
    /// </summary>
    public long ArticleId { get; init; }

    /// <summary>
    /// 父评论 ID，空表示一级评论。
    /// </summary>
    public long? ParentId { get; init; }

    /// <summary>
    /// 评论者昵称。
    /// </summary>
    public string AuthorName { get; init; } = string.Empty;

    /// <summary>
    /// 评论者网站。
    /// </summary>
    public string? AuthorWebsite { get; init; }

    /// <summary>
    /// 评论内容。
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// 公开提交博客评论请求。
/// </summary>
public sealed class CreateBlogCommentRequest
{
    /// <summary>
    /// 文章 ID。
    /// </summary>
    public long ArticleId { get; init; }

    /// <summary>
    /// 父评论 ID，空表示一级评论。
    /// </summary>
    public long? ParentId { get; init; }

    /// <summary>
    /// 评论者昵称。
    /// </summary>
    public string AuthorName { get; init; } = string.Empty;

    /// <summary>
    /// 评论者邮箱。
    /// </summary>
    public string? AuthorEmail { get; init; }

    /// <summary>
    /// 评论者网站。
    /// </summary>
    public string? AuthorWebsite { get; init; }

    /// <summary>
    /// 评论内容。
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 验证码 ID，由获取评论验证码接口返回。
    /// </summary>
    public string CaptchaId { get; init; } = string.Empty;

    /// <summary>
    /// 验证码答案。
    /// </summary>
    public string CaptchaAnswer { get; init; } = string.Empty;
}

/// <summary>
/// 审核博客评论请求。
/// </summary>
public sealed class ReviewBlogCommentRequest
{
    /// <summary>
    /// 审核状态：1=已通过，2=已拒绝，3=垃圾评论。
    /// </summary>
    public int Status { get; init; }
}

/// <summary>
/// 公开提交博客评论结果。
/// </summary>
public sealed class CreateBlogCommentResult
{
    /// <summary>
    /// 评论 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 评论状态，默认 0=待审核。
    /// </summary>
    public int Status { get; init; }
}
