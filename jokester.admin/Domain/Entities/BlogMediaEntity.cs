using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("blog_media")]
public sealed class BlogMediaEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "site_id")]
    public long SiteId { get; set; }

    [SugarColumn(ColumnName = "file_name")]
    public string FileName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "storage_key")]
    public string StorageKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "url")]
    public string Url { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "mime_type")]
    public string MimeType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "file_size")]
    public long FileSize { get; set; }

    [SugarColumn(ColumnName = "width")]
    public int? Width { get; set; }

    [SugarColumn(ColumnName = "height")]
    public int? Height { get; set; }

    [SugarColumn(ColumnName = "storage_provider")]
    public string StorageProvider { get; set; } = "local";

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "created_by")]
    public long? CreatedBy { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
