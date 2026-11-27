using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("site_announcement")]
public sealed class SiteAnnouncementEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }
    [SugarColumn(ColumnName = "title")]
    public string Title { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "summary")]
    public string Summary { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "content")]
    public string? Content { get; set; }
    [SugarColumn(ColumnName = "cover_url")]
    public string? CoverUrl { get; set; }
    [SugarColumn(ColumnName = "category")]
    public string Category { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "version_label")]
    public string? VersionLabel { get; set; }
    [SugarColumn(ColumnName = "is_pinned")]
    public bool IsPinned { get; set; }
    [SugarColumn(ColumnName = "is_important")]
    public bool IsImportant { get; set; }
    [SugarColumn(ColumnName = "link_url")]
    public string? LinkUrl { get; set; }
    [SugarColumn(ColumnName = "link_text")]
    public string? LinkText { get; set; }
    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }
    [SugarColumn(ColumnName = "published_at")]
    public DateTime? PublishedAt { get; set; }
    [SugarColumn(ColumnName = "revision")]
    public int Revision { get; set; }
    [SugarColumn(ColumnName = "created_by")]
    public long CreatedBy { get; set; }
    [SugarColumn(ColumnName = "updated_by")]
    public long? UpdatedBy { get; set; }
    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
