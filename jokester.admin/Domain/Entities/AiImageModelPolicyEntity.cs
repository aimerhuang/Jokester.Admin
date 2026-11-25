using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_model_policy")]
public sealed class AiImageModelPolicyEntity
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "model_code")]
    public string ModelCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "access_level")]
    public string AccessLevel { get; set; } = "all";

    [SugarColumn(ColumnName = "required_tier_code")]
    public string RequiredTierCode { get; set; } = "monthly_vip";

    [SugarColumn(ColumnName = "vip_discount_percent")]
    public int? VipDiscountPercent { get; set; }

    [SugarColumn(ColumnName = "revision")]
    public int Revision { get; set; }

    [SugarColumn(ColumnName = "updated_by")]
    public long? UpdatedBy { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
