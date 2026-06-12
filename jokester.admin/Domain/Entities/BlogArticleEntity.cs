using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("blog_article")]
public sealed class BlogArticleEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "site_id")]
    public long SiteId { get; set; }

    [SugarColumn(ColumnName = "title")]
    public string Title { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "summary")]
    public string? Summary { get; set; }

    [SugarColumn(ColumnName = "content")]
    public string Content { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "cover_url")]
    public string? CoverUrl { get; set; }

    [SugarColumn(ColumnName = "category_id")]
    public long? CategoryId { get; set; }

    [SugarColumn(ColumnName = "tags")]
    public string? Tags { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "view_count")]
    public int ViewCount { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "created_by")]
    public long? CreatedBy { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_by")]
    public long? UpdatedBy { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
