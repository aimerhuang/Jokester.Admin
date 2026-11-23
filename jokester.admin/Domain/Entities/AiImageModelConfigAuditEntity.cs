using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_model_config_audit")]
public sealed class AiImageModelConfigAuditEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "config_id")]
    public long ConfigId { get; set; }

    [SugarColumn(ColumnName = "model_code")]
    public string ModelCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "resolution_code")]
    public string ResolutionCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "route_role")]
    public string RouteRole { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "action")]
    public string Action { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "changed_fields")]
    public string ChangedFields { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "before_json")]
    public string? BeforeJson { get; set; }

    [SugarColumn(ColumnName = "after_json")]
    public string? AfterJson { get; set; }

    [SugarColumn(ColumnName = "operator_user_id")]
    public long OperatorUserId { get; set; }

    [SugarColumn(ColumnName = "operator_ip")]
    public string? OperatorIp { get; set; }

    /// <summary>UTC.</summary>
    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
