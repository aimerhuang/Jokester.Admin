using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_task_result")]
public sealed class AiImageTaskResultEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "task_id")]
    public long TaskId { get; set; }

    [SugarColumn(ColumnName = "result_ordinal")]
    public int ResultOrdinal { get; set; }

    [SugarColumn(ColumnName = "url")]
    public string Url { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "width")]
    public int Width { get; set; }

    [SugarColumn(ColumnName = "height")]
    public int Height { get; set; }

    [SugarColumn(ColumnName = "size")]
    public string Size { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "mime_type")]
    public string MimeType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "is_quarantined")]
    public bool IsQuarantined { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
