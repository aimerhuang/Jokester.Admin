using jokester.admin.Application.DTOs.Common;

namespace jokester.admin.Application.DTOs.Menus;

/// <summary>
/// 菜单树节点。
/// </summary>
public sealed class MenuTreeNodeDto
{
    /// <summary>
    /// 菜单 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 所属站点 ID。
    /// </summary>
    public long SiteId { get; init; }

    /// <summary>
    /// 父级菜单 ID，0 表示根节点。
    /// </summary>
    public long ParentId { get; init; }

    /// <summary>
    /// 菜单名称。
    /// </summary>
    public string MenuName { get; init; } = string.Empty;

    /// <summary>
    /// 菜单编码。
    /// </summary>
    public string? MenuCode { get; init; }

    /// <summary>
    /// 权限码。
    /// </summary>
    public string? PermissionCode { get; init; }

    /// <summary>
    /// 菜单类型：1=目录，2=菜单，3=按钮，4=接口权限。
    /// </summary>
    public int MenuType { get; init; }

    /// <summary>
    /// 前端路由路径。
    /// </summary>
    public string? RoutePath { get; init; }

    /// <summary>
    /// 前端组件路径。
    /// </summary>
    public string? Component { get; init; }

    /// <summary>
    /// 图标。
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// 排序值。
    /// </summary>
    public int Sort { get; init; }

    /// <summary>
    /// 是否显示。
    /// </summary>
    public bool Visible { get; init; }

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; }

    /// <summary>
    /// 是否缓存页面。
    /// </summary>
    public bool KeepAlive { get; init; }

    /// <summary>
    /// 是否外链。
    /// </summary>
    public bool IsExternal { get; init; }

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; init; }

    /// <summary>
    /// 子菜单列表。
    /// </summary>
    public IReadOnlyCollection<MenuTreeNodeDto> Children { get; init; } = Array.Empty<MenuTreeNodeDto>();
}

/// <summary>
/// 保存菜单请求。
/// </summary>
public sealed class SaveMenuRequest
{
    /// <summary>
    /// 所属站点 ID。
    /// </summary>
    public long SiteId { get; init; }

    /// <summary>
    /// 父级菜单 ID，0 表示根节点。
    /// </summary>
    public long ParentId { get; init; }

    /// <summary>
    /// 菜单名称。
    /// </summary>
    public string MenuName { get; init; } = string.Empty;

    /// <summary>
    /// 菜单编码。
    /// </summary>
    public string? MenuCode { get; init; }

    /// <summary>
    /// 菜单类型：1=目录，2=菜单，3=按钮，4=接口权限。
    /// </summary>
    public int MenuType { get; init; }

    /// <summary>
    /// 前端路由路径。
    /// </summary>
    public string? RoutePath { get; init; }

    /// <summary>
    /// 前端组件路径。
    /// </summary>
    public string? Component { get; init; }

    /// <summary>
    /// 权限码。
    /// </summary>
    public string? PermissionCode { get; init; }

    /// <summary>
    /// 图标。
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// 排序值。
    /// </summary>
    public int Sort { get; init; }

    /// <summary>
    /// 是否显示。
    /// </summary>
    public bool Visible { get; init; } = true;

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; } = 1;

    /// <summary>
    /// 是否缓存页面。
    /// </summary>
    public bool KeepAlive { get; init; }

    /// <summary>
    /// 是否外链。
    /// </summary>
    public bool IsExternal { get; init; }

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; init; }
}

/// <summary>
/// 菜单分页查询参数。
/// </summary>
public sealed class MenuQuery : PageQuery
{
    /// <summary>
    /// 站点 ID。
    /// </summary>
    public long? SiteId { get; init; }

    /// <summary>
    /// 父级菜单 ID。
    /// </summary>
    public long? ParentId { get; init; }

    /// <summary>
    /// 关键词，匹配菜单名称、编码或权限码。
    /// </summary>
    public string? Keyword { get; init; }

    /// <summary>
    /// 菜单类型：1=目录，2=菜单，3=按钮，4=接口权限。
    /// </summary>
    public int? MenuType { get; init; }

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int? Status { get; init; }
}

/// <summary>
/// 菜单列表项。
/// </summary>
public sealed class MenuListItemDto
{
    /// <summary>
    /// 菜单 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 所属站点 ID。
    /// </summary>
    public long SiteId { get; init; }

    /// <summary>
    /// 父级菜单 ID，0 表示根节点。
    /// </summary>
    public long ParentId { get; init; }

    /// <summary>
    /// 菜单名称。
    /// </summary>
    public string MenuName { get; init; } = string.Empty;

    /// <summary>
    /// 菜单编码。
    /// </summary>
    public string? MenuCode { get; init; }

    /// <summary>
    /// 权限码。
    /// </summary>
    public string? PermissionCode { get; init; }

    /// <summary>
    /// 菜单类型：1=目录，2=菜单，3=按钮，4=接口权限。
    /// </summary>
    public int MenuType { get; init; }

    /// <summary>
    /// 前端路由路径。
    /// </summary>
    public string? RoutePath { get; init; }

    /// <summary>
    /// 前端组件路径。
    /// </summary>
    public string? Component { get; init; }

    /// <summary>
    /// 图标。
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// 排序值。
    /// </summary>
    public int Sort { get; init; }

    /// <summary>
    /// 是否显示。
    /// </summary>
    public bool Visible { get; init; }

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; }

    /// <summary>
    /// 是否缓存页面。
    /// </summary>
    public bool KeepAlive { get; init; }

    /// <summary>
    /// 是否外链。
    /// </summary>
    public bool IsExternal { get; init; }

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; init; }
}

/// <summary>
/// 修改菜单状态请求。
/// </summary>
public sealed class UpdateMenuStatusRequest
{
    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; }
}
