using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_request_task")]
public sealed class AiImageRequestTaskEntity
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "request_id")]
    public long RequestId { get; set; }

    [SugarColumn(IsPrimaryKey = true, ColumnName = "task_ordinal")]
    public int TaskOrdinal { get; set; }

    [SugarColumn(ColumnName = "task_id")]
    public long TaskId { get; set; }
}
