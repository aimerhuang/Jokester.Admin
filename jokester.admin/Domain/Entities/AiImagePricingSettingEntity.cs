using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_pricing_setting")]
public sealed class AiImagePricingSettingEntity
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "default_vip_discount_percent")]
    public int DefaultVipDiscountPercent { get; set; } = 80;

    [SugarColumn(ColumnName = "revision")]
    public int Revision { get; set; }

    [SugarColumn(ColumnName = "pricing_revision")]
    public long PricingRevision { get; set; } = 1;

    [SugarColumn(ColumnName = "updated_by")]
    public long? UpdatedBy { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
