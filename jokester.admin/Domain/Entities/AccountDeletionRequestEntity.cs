using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("account_deletion_request")]
public sealed class AccountDeletionRequestEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "request_id")]
    public string RequestId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "client_request_hash")]
    public string ClientRequestHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "request_fingerprint")]
    public string RequestFingerprint { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "reason")]
    public string? Reason { get; set; }

    [SugarColumn(ColumnName = "notification_email")]
    public string? NotificationEmail { get; set; }

    [SugarColumn(ColumnName = "requested_at")]
    public DateTime RequestedAt { get; set; }

    [SugarColumn(ColumnName = "scheduled_deletion_at")]
    public DateTime ScheduledDeletionAt { get; set; }

    [SugarColumn(ColumnName = "cancelled_at")]
    public DateTime? CancelledAt { get; set; }

    [SugarColumn(ColumnName = "data_deleted_at")]
    public DateTime? DataDeletedAt { get; set; }

    [SugarColumn(ColumnName = "completed_at")]
    public DateTime? CompletedAt { get; set; }

    [SugarColumn(ColumnName = "next_retry_at")]
    public DateTime? NextRetryAt { get; set; }

    [SugarColumn(ColumnName = "retry_count")]
    public int RetryCount { get; set; }

    [SugarColumn(ColumnName = "failure_message")]
    public string? FailureMessage { get; set; }

    [SugarColumn(ColumnName = "notification_sent_at")]
    public DateTime? NotificationSentAt { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
