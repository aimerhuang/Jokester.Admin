using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_prompt_sensitive_word_revision")]
public sealed class AiPromptSensitiveWordRevisionEntity
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "revision")]
    public long Revision { get; set; }

    [SugarColumn(ColumnName = "updated_by")]
    public long? UpdatedBy { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime UpdatedAt { get; set; }
}
