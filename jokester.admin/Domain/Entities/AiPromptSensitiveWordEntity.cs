using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_prompt_sensitive_word")]
public sealed class AiPromptSensitiveWordEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "term")]
    public string Term { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "normalized_term")]
    public string NormalizedTerm { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "term_key")]
    public string TermKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "language_code")]
    public string LanguageCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "category_code")]
    public string CategoryCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "match_mode")]
    public string MatchMode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "action")]
    public string Action { get; set; } = "block";

    [SugarColumn(ColumnName = "severity")]
    public int Severity { get; set; } = 1;

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; } = 1;

    [SugarColumn(ColumnName = "source_code")]
    public string? SourceCode { get; set; }

    [SugarColumn(ColumnName = "source_version")]
    public string? SourceVersion { get; set; }

    [SugarColumn(ColumnName = "remark")]
    public string? Remark { get; set; }

    [SugarColumn(ColumnName = "created_by")]
    public long? CreatedBy { get; set; }

    [SugarColumn(ColumnName = "updated_by")]
    public long? UpdatedBy { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
