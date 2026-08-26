using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_model_release_price")]
public sealed class AiImageModelReleasePriceEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "model_release_id")]
    public long ModelReleaseId { get; set; }

    [SugarColumn(ColumnName = "model_code")]
    public string ModelCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "pricing_mode")]
    public string PricingMode { get; set; } = "explicit";

    [SugarColumn(ColumnName = "resolution_code")]
    public string ResolutionCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "quality_code")]
    public string QualityCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "points")]
    public int Points { get; set; }

    [SugarColumn(ColumnName = "price_amount")]
    public decimal PriceAmount { get; set; }

    [SugarColumn(ColumnName = "currency")]
    public string Currency { get; set; } = "CNY";

    [SugarColumn(ColumnName = "sort")]
    public int Sort { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; } = 1;
}
