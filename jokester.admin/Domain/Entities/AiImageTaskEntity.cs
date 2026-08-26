using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_task")]
public sealed class AiImageTaskEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "site_id")]
    public long SiteId { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "source_prompt_id")]
    public long? SourcePromptId { get; set; }

    [SugarColumn(ColumnName = "prompt")]
    public string Prompt { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "negative_prompt")]
    public string? NegativePrompt { get; set; }

    [SugarColumn(ColumnName = "prompt_policy_version")]
    public long PromptPolicyVersion { get; set; }

    [SugarColumn(ColumnName = "prompt_checked_at")]
    public DateTime? PromptCheckedAt { get; set; }

    [SugarColumn(ColumnName = "model_name")]
    public string? ModelName { get; set; }

    [SugarColumn(ColumnName = "model_code")]
    public string? ModelCode { get; set; }

    [SugarColumn(ColumnName = "size_contract_version")]
    public string? SizeContractVersion { get; set; }

    [SugarColumn(ColumnName = "size_mode")]
    public string? SizeMode { get; set; }

    [SugarColumn(ColumnName = "requested_size")]
    public string? RequestedSize { get; set; }

    [SugarColumn(ColumnName = "requested_width")]
    public int? RequestedWidth { get; set; }

    [SugarColumn(ColumnName = "requested_height")]
    public int? RequestedHeight { get; set; }

    [SugarColumn(ColumnName = "output_width")]
    public int? OutputWidth { get; set; }

    [SugarColumn(ColumnName = "output_height")]
    public int? OutputHeight { get; set; }

    [SugarColumn(ColumnName = "output_size")]
    public string? OutputSize { get; set; }

    [SugarColumn(ColumnName = "output_mime_type")]
    public string? OutputMimeType { get; set; }

    [SugarColumn(ColumnName = "model_release_id")]
    public long? ModelReleaseId { get; set; }

    [SugarColumn(ColumnName = "price_id")]
    public long? PriceId { get; set; }

    [SugarColumn(ColumnName = "price_release_id")]
    public long? PriceReleaseId { get; set; }

    [SugarColumn(ColumnName = "unit_point_cost")]
    public int? UnitPointCost { get; set; }

    [SugarColumn(ColumnName = "image_count")]
    public int ImageCount { get; set; } = 1;

    [SugarColumn(ColumnName = "completed_image_count")]
    public int CompletedImageCount { get; set; }

    [SugarColumn(ColumnName = "idempotency_key")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "request_fingerprint")]
    public string RequestFingerprint { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "point_cost")]
    public int PointCost { get; set; }

    [SugarColumn(ColumnName = "billing_status")]
    public int BillingStatus { get; set; }

    [SugarColumn(ColumnName = "refunded_points")]
    public int? RefundedPoints { get; set; }

    [SugarColumn(ColumnName = "resolution_code")]
    public string? ResolutionCode { get; set; }

    [SugarColumn(ColumnName = "quality_code")]
    public string QualityCode { get; set; } = "med";

    [SugarColumn(ColumnName = "aspect_ratio_code")]
    public string? AspectRatioCode { get; set; }

    [SugarColumn(ColumnName = "width")]
    public int Width { get; set; } = 1024;

    [SugarColumn(ColumnName = "height")]
    public int Height { get; set; } = 1024;

    [SugarColumn(ColumnName = "size")]
    public string Size { get; set; } = "1024x1024";

    [SugarColumn(ColumnName = "quality")]
    public string Quality { get; set; } = "medium";

    [SugarColumn(ColumnName = "reference_image_urls")]
    public string? ReferenceImageUrls { get; set; }

    [SugarColumn(ColumnName = "mask_image_url")]
    public string? MaskImageUrl { get; set; }

    [SugarColumn(ColumnName = "result_urls")]
    public string? ResultUrls { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "error_message")]
    public string? ErrorMessage { get; set; }

    [SugarColumn(ColumnName = "failure_code")]
    public string? FailureCode { get; set; }

    [SugarColumn(ColumnName = "failure_stage")]
    public string? FailureStage { get; set; }

    [SugarColumn(ColumnName = "retryable")]
    public bool? Retryable { get; set; }

    [SugarColumn(ColumnName = "claim_epoch")]
    public long ClaimEpoch { get; set; }

    [SugarColumn(ColumnName = "claim_token_hash")]
    public string? ClaimTokenHash { get; set; }

    [SugarColumn(ColumnName = "lease_expires_at")]
    public DateTime? LeaseExpiresAt { get; set; }

    [SugarColumn(ColumnName = "heartbeat_at")]
    public DateTime? HeartbeatAt { get; set; }

    [SugarColumn(ColumnName = "started_at")]
    public DateTime? StartedAt { get; set; }

    [SugarColumn(ColumnName = "completed_at")]
    public DateTime? CompletedAt { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
