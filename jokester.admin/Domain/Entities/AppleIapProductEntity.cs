using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("apple_iap_product")]
public sealed class AppleIapProductEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "package_id")]
    public long PackageId { get; set; }

    [SugarColumn(ColumnName = "package_code")]
    public string PackageCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "apple_product_id")]
    public string AppleProductId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "product_type")]
    public string ProductType { get; set; } = "consumable";

    [SugarColumn(ColumnName = "points")]
    public int Points { get; set; }

    [SugarColumn(ColumnName = "environment")]
    public string Environment { get; set; } = "Production";

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; } = 1;

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
