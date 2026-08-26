using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("point_recharge_order")]
public sealed class PointRechargeOrderEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "order_no")]
    public string OrderNo { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "package_id")]
    public long PackageId { get; set; }

    [SugarColumn(ColumnName = "package_code")]
    public string PackageCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "points")]
    public int Points { get; set; }

    [SugarColumn(ColumnName = "point_validity_days")]
    public int? PointValidityDays { get; set; }

    [SugarColumn(ColumnName = "price_amount")]
    public decimal PriceAmount { get; set; }

    [SugarColumn(ColumnName = "currency")]
    public string Currency { get; set; } = "CNY";

    [SugarColumn(ColumnName = "purchase_url")]
    public string? PurchaseUrl { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "expires_at")]
    public DateTime ExpiresAt { get; set; }

    [SugarColumn(ColumnName = "paid_at")]
    public DateTime? PaidAt { get; set; }

    [SugarColumn(ColumnName = "fulfilled_at")]
    public DateTime? FulfilledAt { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
