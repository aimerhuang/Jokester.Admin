using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_user_role")]
public sealed class SysUserRoleEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "role_id")]
    public long RoleId { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
