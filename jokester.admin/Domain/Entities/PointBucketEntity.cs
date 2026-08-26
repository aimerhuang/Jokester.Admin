using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_user_point_bucket")]
public sealed class PointBucketEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "source")]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "business_key")]
    public string BusinessKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "granted_points")]
    public int GrantedPoints { get; set; }

    [SugarColumn(ColumnName = "remaining_points")]
    public int RemainingPoints { get; set; }

    [SugarColumn(ColumnName = "expired_points")]
    public int ExpiredPoints { get; set; }

    [SugarColumn(ColumnName = "expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [SugarColumn(ColumnName = "spend_priority")]
    public int SpendPriority { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
