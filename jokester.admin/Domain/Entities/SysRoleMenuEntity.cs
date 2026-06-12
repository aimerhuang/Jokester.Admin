using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_role_menu")]
public sealed class SysRoleMenuEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "role_id")]
    public long RoleId { get; set; }

    [SugarColumn(ColumnName = "menu_id")]
    public long MenuId { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
