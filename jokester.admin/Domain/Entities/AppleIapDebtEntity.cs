using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("apple_iap_debt")]
public sealed class AppleIapDebtEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "transaction_id")]
    public string TransactionId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "points_owed")]
    public int PointsOwed { get; set; }

    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = "open";

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
