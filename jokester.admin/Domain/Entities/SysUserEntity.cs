using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_user")]
public sealed class SysUserEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "user_name")]
    public string UserName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "nick_name")]
    public string? NickName { get; set; }

    [SugarColumn(ColumnName = "password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "salt")]
    public string? Salt { get; set; }

    [SugarColumn(ColumnName = "email")]
    public string? Email { get; set; }

    [SugarColumn(ColumnName = "phone")]
    public string? Phone { get; set; }

    [SugarColumn(ColumnName = "avatar_url")]
    public string? AvatarUrl { get; set; }

    [SugarColumn(ColumnName = "signature")]
    public string? Signature { get; set; }

    [SugarColumn(ColumnName = "point_balance")]
    public int PointBalance { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "is_super_admin")]
    public bool IsSuperAdmin { get; set; }

    [SugarColumn(ColumnName = "last_login_time")]
    public DateTime? LastLoginTime { get; set; }

    [SugarColumn(ColumnName = "last_login_ip")]
    public string? LastLoginIp { get; set; }

    [SugarColumn(ColumnName = "remark")]
    public string? Remark { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
