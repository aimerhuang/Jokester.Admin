using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_model_release")]
public sealed class AiImageModelReleaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "model_code")]
    public string ModelCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "model_name")]
    public string ModelName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "catalog_version")]
    public string CatalogVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "size_contract_version")]
    public string SizeContractVersion { get; set; } = "legacy-explicit-v1";

    [SugarColumn(ColumnName = "default_size_mode")]
    public string DefaultSizeMode { get; set; } = "explicit";

    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = "draft";

    [SugarColumn(ColumnName = "revoked_at")]
    public DateTime? RevokedAt { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "published_at")]
    public DateTime? PublishedAt { get; set; }
}
