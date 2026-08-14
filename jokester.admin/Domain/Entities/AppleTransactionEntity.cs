using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("apple_transaction")]
public sealed class AppleTransactionEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "transaction_id")]
    public string TransactionId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "idempotency_key_hash")]
    public string IdempotencyKeyHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "request_fingerprint")]
    public string RequestFingerprint { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "original_transaction_id")]
    public string OriginalTransactionId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "product_id")]
    public string ProductId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "package_id")]
    public long PackageId { get; set; }

    [SugarColumn(ColumnName = "order_no")]
    public string OrderNo { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "environment")]
    public string Environment { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "app_account_token")]
    public string AppAccountToken { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "points")]
    public int Points { get; set; }

    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "signed_transaction_hash")]
    public string SignedTransactionHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "purchase_date")]
    public DateTime PurchaseDate { get; set; }

    [SugarColumn(ColumnName = "revocation_date")]
    public DateTime? RevocationDate { get; set; }

    [SugarColumn(ColumnName = "fulfilled_at")]
    public DateTime? FulfilledAt { get; set; }

    [SugarColumn(ColumnName = "refunded_at")]
    public DateTime? RefundedAt { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
