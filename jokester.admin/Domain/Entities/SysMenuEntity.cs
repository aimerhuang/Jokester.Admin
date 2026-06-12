using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_menu")]
public sealed class SysMenuEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "site_id")]
    public long SiteId { get; set; }

    [SugarColumn(ColumnName = "parent_id")]
    public long ParentId { get; set; }

    [SugarColumn(ColumnName = "menu_name")]
    public string MenuName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "menu_code")]
    public string? MenuCode { get; set; }

    [SugarColumn(ColumnName = "menu_type")]
    public int MenuType { get; set; }

    [SugarColumn(ColumnName = "route_path")]
    public string? RoutePath { get; set; }

    [SugarColumn(ColumnName = "component")]
    public string? Component { get; set; }

    [SugarColumn(ColumnName = "permission_code")]
    public string? PermissionCode { get; set; }

    [SugarColumn(ColumnName = "icon")]
    public string? Icon { get; set; }

    [SugarColumn(ColumnName = "sort")]
    public int Sort { get; set; }

    [SugarColumn(ColumnName = "visible")]
    public bool Visible { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "keep_alive")]
    public bool KeepAlive { get; set; }

    [SugarColumn(ColumnName = "is_external")]
    public bool IsExternal { get; set; }

    [SugarColumn(ColumnName = "remark")]
    public string? Remark { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
