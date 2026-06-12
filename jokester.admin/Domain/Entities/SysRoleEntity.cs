using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_role")]
public sealed class SysRoleEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "role_name")]
    public string RoleName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "role_code")]
    public string RoleCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "remark")]
    public string? Remark { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
