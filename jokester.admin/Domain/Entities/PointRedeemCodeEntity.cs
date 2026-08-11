using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("point_redeem_code")]
public sealed class PointRedeemCodeEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "code_hash")]
    public string CodeHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "code_mask")]
    public string CodeMask { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "package_id")]
    public long? PackageId { get; set; }

    [SugarColumn(ColumnName = "order_id")]
    public long? OrderId { get; set; }

    [SugarColumn(ColumnName = "points")]
    public int Points { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "redeemed_by_user_id")]
    public long? RedeemedByUserId { get; set; }

    [SugarColumn(ColumnName = "expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [SugarColumn(ColumnName = "redeemed_at")]
    public DateTime? RedeemedAt { get; set; }

    [SugarColumn(ColumnName = "created_by")]
    public long? CreatedBy { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
