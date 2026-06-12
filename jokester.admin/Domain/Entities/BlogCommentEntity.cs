using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("blog_comment")]
public sealed class BlogCommentEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "site_id")]
    public long SiteId { get; set; }

    [SugarColumn(ColumnName = "article_id")]
    public long ArticleId { get; set; }

    [SugarColumn(ColumnName = "parent_id")]
    public long? ParentId { get; set; }

    [SugarColumn(ColumnName = "author_name")]
    public string AuthorName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "author_email")]
    public string? AuthorEmail { get; set; }

    [SugarColumn(ColumnName = "author_website")]
    public string? AuthorWebsite { get; set; }

    [SugarColumn(ColumnName = "content")]
    public string Content { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "ip_address")]
    public string? IpAddress { get; set; }

    [SugarColumn(ColumnName = "user_agent")]
    public string? UserAgent { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "reviewed_at")]
    public DateTime? ReviewedAt { get; set; }

    [SugarColumn(ColumnName = "reviewed_by")]
    public long? ReviewedBy { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
