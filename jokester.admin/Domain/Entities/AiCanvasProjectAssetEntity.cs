using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_canvas_project_asset")]
public sealed class AiCanvasProjectAssetEntity
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "canvas_project_id")]
    public long CanvasProjectId { get; set; }

    [SugarColumn(IsPrimaryKey = true, ColumnName = "asset_id")]
    public string AssetId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "owner_user_id")]
    public long OwnerUserId { get; set; }
}
