using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_point_price")]
public sealed class AiImagePointPriceEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "model_code")]
    public string ModelCode { get; set; } = string.Empty;

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

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
