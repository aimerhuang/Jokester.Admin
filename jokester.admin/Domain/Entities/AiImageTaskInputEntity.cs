using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_task_input")]
public sealed class AiImageTaskInputEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "task_id")]
    public long TaskId { get; set; }

    [SugarColumn(IsIgnore = true)]
    public int RequestTaskOrdinal { get; set; }

    [SugarColumn(ColumnName = "role")]
    public string Role { get; set; } = "reference";

    [SugarColumn(ColumnName = "input_ordinal")]
    public int InputOrdinal { get; set; }

    [SugarColumn(ColumnName = "input_kind")]
    public string InputKind { get; set; } = "asset";

    [SugarColumn(ColumnName = "asset_id")]
    public string? AssetId { get; set; }

    [SugarColumn(ColumnName = "owner_user_id")]
    public long OwnerUserId { get; set; }

    [SugarColumn(ColumnName = "storage_key")]
    public string? StorageKey { get; set; }

    [SugarColumn(ColumnName = "content_sha256")]
    public string? ContentSha256 { get; set; }

    [SugarColumn(ColumnName = "legacy_url")]
    public string? LegacyUrl { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
