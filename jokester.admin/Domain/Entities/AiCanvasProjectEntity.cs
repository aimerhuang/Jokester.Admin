using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_canvas_project")]
public sealed class AiCanvasProjectEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "canvas_id")]
    public string CanvasId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "owner_user_id")]
    public long OwnerUserId { get; set; }

    [SugarColumn(ColumnName = "title")]
    public string Title { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "schema_version")]
    public int SchemaVersion { get; set; }

    [SugarColumn(ColumnName = "revision")]
    public long Revision { get; set; }

    [SugarColumn(ColumnName = "document")]
    public string Document { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "document_bytes")]
    public int DocumentBytes { get; set; }

    [SugarColumn(ColumnName = "node_count")]
    public int NodeCount { get; set; }

    [SugarColumn(ColumnName = "connection_count")]
    public int ConnectionCount { get; set; }

    [SugarColumn(ColumnName = "cover_asset_id")]
    public string? CoverAssetId { get; set; }

    [SugarColumn(ColumnName = "storage_bytes")]
    public long StorageBytes { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
