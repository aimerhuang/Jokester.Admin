using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_task_outbox")]
public sealed class AiImageTaskOutboxEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "request_id")]
    public long RequestId { get; set; }

    [SugarColumn(ColumnName = "task_id")]
    public long TaskId { get; set; }

    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = "pending";

    [SugarColumn(ColumnName = "attempt_count")]
    public int AttemptCount { get; set; }

    [SugarColumn(ColumnName = "next_attempt_at")]
    public DateTime NextAttemptAt { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
