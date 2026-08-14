using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("media_asset")]
public sealed class MediaAssetEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "asset_id")]
    public string AssetId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "owner_user_id")]
    public long OwnerUserId { get; set; }

    [SugarColumn(ColumnName = "asset_type")]
    public string AssetType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "storage_key")]
    public string StorageKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "thumbnail_key")]
    public string? ThumbnailKey { get; set; }

    [SugarColumn(ColumnName = "mime_type")]
    public string MimeType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "width")]
    public int Width { get; set; }

    [SugarColumn(ColumnName = "height")]
    public int Height { get; set; }

    [SugarColumn(ColumnName = "size_bytes")]
    public long SizeBytes { get; set; }

    [SugarColumn(ColumnName = "sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "metadata_stripped")]
    public bool MetadataStripped { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
