using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_request_idempotency")]
public sealed class AiImageRequestEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "idempotency_key_hash")]
    public string IdempotencyKeyHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "canonical_payload_hash")]
    public string CanonicalPayloadHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "canonicalization_version")]
    public string CanonicalizationVersion { get; set; } = "size-mode-v1";

    [SugarColumn(ColumnName = "normalization_profile")]
    public string NormalizationProfile { get; set; } = "native-v1";

    [SugarColumn(ColumnName = "size_contract_version")]
    public string SizeContractVersion { get; set; } = "size-mode-v1";

    [SugarColumn(ColumnName = "model_release_id")]
    public long? ModelReleaseId { get; set; }

    [SugarColumn(ColumnName = "admission_reservation_id")]
    public string? AdmissionReservationId { get; set; }

    [SugarColumn(ColumnName = "admission_quota_date")]
    public string? AdmissionQuotaDate { get; set; }

    [SugarColumn(ColumnName = "reserved_point_cost")]
    public int ReservedPointCost { get; set; }

    [SugarColumn(ColumnName = "requested_image_count")]
    public int RequestedImageCount { get; set; }

    [SugarColumn(ColumnName = "task_count")]
    public int TaskCount { get; set; }

    [SugarColumn(ColumnName = "legacy_batch_shape")]
    public string LegacyBatchShape { get; set; } = "split-task-per-image";

    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = "active";

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
