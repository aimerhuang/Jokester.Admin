using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("legal_document")]
public sealed class LegalDocumentEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "document_type")]
    public string DocumentType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "version")]
    public string Version { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "platform")]
    public string Platform { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "locale")]
    public string Locale { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "url")]
    public string Url { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "provider_codes_json")]
    public string? ProviderCodesJson { get; set; }

    [SugarColumn(ColumnName = "effective_at")]
    public DateTime EffectiveAt { get; set; }

    [SugarColumn(ColumnName = "requires_reconsent")]
    public bool RequiresReconsent { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; } = 1;

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
