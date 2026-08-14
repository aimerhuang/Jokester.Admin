using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("user_consent")]
public sealed class UserConsentEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "consent_type")]
    public string ConsentType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "document_version")]
    public string DocumentVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "provider_codes_json")]
    public string? ProviderCodesJson { get; set; }

    [SugarColumn(ColumnName = "accepted")]
    public bool Accepted { get; set; }

    [SugarColumn(ColumnName = "client_platform")]
    public string ClientPlatform { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "accepted_at")]
    public DateTime? AcceptedAt { get; set; }

    [SugarColumn(ColumnName = "revoked_at")]
    public DateTime? RevokedAt { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
