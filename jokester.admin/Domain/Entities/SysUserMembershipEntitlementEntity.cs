using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_user_membership_entitlement")]
public sealed class SysUserMembershipEntitlementEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "tier_code")]
    public string TierCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "source")]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "business_key")]
    public string BusinessKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "starts_at")]
    public DateTime StartsAt { get; set; }

    [SugarColumn(ColumnName = "expires_at")]
    public DateTime ExpiresAt { get; set; }

    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "revoked_at")]
    public DateTime? RevokedAt { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
