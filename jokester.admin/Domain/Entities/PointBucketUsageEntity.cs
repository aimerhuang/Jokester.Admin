using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_user_point_bucket_usage")]
public sealed class PointBucketUsageEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "bucket_id")]
    public long BucketId { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "business_key")]
    public string BusinessKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "used_points")]
    public int UsedPoints { get; set; }

    [SugarColumn(ColumnName = "refunded_points")]
    public int RefundedPoints { get; set; }

    [SugarColumn(ColumnName = "deferred_clawback_points")]
    public int DeferredClawbackPoints { get; set; }

    [SugarColumn(ColumnName = "deferred_clawback_business_key")]
    public string? DeferredClawbackBusinessKey { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
