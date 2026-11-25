using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_pricing_audit")]
public sealed class AiImagePricingAuditEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "target_type")]
    public string TargetType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "target_id")]
    public string TargetId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "model_code")]
    public string? ModelCode { get; set; }

    [SugarColumn(ColumnName = "resolution_code")]
    public string? ResolutionCode { get; set; }

    [SugarColumn(ColumnName = "quality_code")]
    public string? QualityCode { get; set; }

    [SugarColumn(ColumnName = "action")]
    public string Action { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "before_json")]
    public string? BeforeJson { get; set; }

    [SugarColumn(ColumnName = "after_json")]
    public string? AfterJson { get; set; }

    [SugarColumn(ColumnName = "reason")]
    public string Reason { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "operator_user_id")]
    public long OperatorUserId { get; set; }

    [SugarColumn(ColumnName = "operator_ip")]
    public string? OperatorIp { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
