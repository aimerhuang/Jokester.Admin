using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("point_recharge_package")]
public sealed class PointRechargePackageEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "package_code")]
    public string PackageCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }

    [SugarColumn(ColumnName = "points")]
    public int Points { get; set; }

    [SugarColumn(ColumnName = "repeat_points")]
    public int? RepeatPoints { get; set; }

    [SugarColumn(ColumnName = "price_amount")]
    public decimal PriceAmount { get; set; }

    [SugarColumn(ColumnName = "currency")]
    public string Currency { get; set; } = "CNY";

    [SugarColumn(ColumnName = "validity_days")]
    public int? ValidityDays { get; set; }

    [SugarColumn(ColumnName = "bonus_percent")]
    public int BonusPercent { get; set; }

    [SugarColumn(ColumnName = "badge_code")]
    public string? BadgeCode { get; set; }

    [SugarColumn(ColumnName = "benefits_json")]
    public string? BenefitsJson { get; set; }

    [SugarColumn(ColumnName = "purchase_url")]
    public string? PurchaseUrl { get; set; }

    [SugarColumn(ColumnName = "is_featured")]
    public bool IsFeatured { get; set; }

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
