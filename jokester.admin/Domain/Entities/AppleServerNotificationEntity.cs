using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("apple_server_notification")]
public sealed class AppleServerNotificationEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "notification_uuid")]
    public string NotificationUuid { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "notification_type")]
    public string NotificationType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "subtype")]
    public string? Subtype { get; set; }

    [SugarColumn(ColumnName = "environment")]
    public string Environment { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "transaction_id")]
    public string? TransactionId { get; set; }

    [SugarColumn(ColumnName = "signed_payload_hash")]
    public string SignedPayloadHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "retry_count")]
    public int RetryCount { get; set; }

    [SugarColumn(ColumnName = "failure_message")]
    public string? FailureMessage { get; set; }

    [SugarColumn(ColumnName = "received_at")]
    public DateTime ReceivedAt { get; set; }

    [SugarColumn(ColumnName = "processed_at")]
    public DateTime? ProcessedAt { get; set; }
}
