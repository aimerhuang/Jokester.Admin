using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_model_current_release")]
public sealed class AiImageCurrentReleaseEntity
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "model_code")]
    public string ModelCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "model_release_id")]
    public long ModelReleaseId { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime UpdatedAt { get; set; }
}
