using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_provider_attempt")]
public sealed class AiImageProviderAttemptEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "attempt_id")]
    public string AttemptId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "task_id")]
    public long TaskId { get; set; }

    [SugarColumn(ColumnName = "claim_epoch")]
    public long ClaimEpoch { get; set; }

    [SugarColumn(ColumnName = "model_release_id")]
    public long? ModelReleaseId { get; set; }

    [SugarColumn(ColumnName = "release_route_id")]
    public long? ReleaseRouteId { get; set; }

    [SugarColumn(ColumnName = "route_role")]
    public string? RouteRole { get; set; }

    [SugarColumn(ColumnName = "consent_provider_code")]
    public string? ConsentProviderCode { get; set; }

    [SugarColumn(ColumnName = "upstream_idempotency_key")]
    public string UpstreamIdempotencyKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "state")]
    public string State { get; set; } = "prepared";

    [SugarColumn(ColumnName = "started_at")]
    public DateTime StartedAt { get; set; }

    [SugarColumn(ColumnName = "deadline")]
    public DateTime Deadline { get; set; }

    [SugarColumn(ColumnName = "reconcile_by")]
    public DateTime ReconcileBy { get; set; }

    [SugarColumn(ColumnName = "completed_at")]
    public DateTime? CompletedAt { get; set; }
}
