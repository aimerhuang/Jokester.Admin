using jokester.admin.Application.DTOs.Common;

namespace jokester.admin.Application.DTOs.Roles;

/// <summary>
/// 角色信息。
/// </summary>
public sealed class RoleDto
{
    /// <summary>
    /// 角色 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 角色名称。
    /// </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary>
    /// 角色编码。
    /// </summary>
    public string RoleCode { get; init; } = string.Empty;

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; }

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; init; }

    /// <summary>
    /// 已分配菜单 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> MenuIds { get; init; } = Array.Empty<long>();
}

/// <summary>
/// 角色分页查询参数。
/// </summary>
public sealed class RoleQuery : PageQuery
{
    /// <summary>
    /// 关键词，匹配角色名称或编码。
    /// </summary>
    public string? Keyword { get; init; }

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int? Status { get; init; }
}

/// <summary>
/// 保存角色请求。
/// </summary>
public sealed class SaveRoleRequest
{
    /// <summary>
    /// 角色名称。
    /// </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary>
    /// 角色编码。
    /// </summary>
    public string RoleCode { get; init; } = string.Empty;

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; } = 1;

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; init; }
}

/// <summary>
/// 分配角色菜单请求。
/// </summary>
public sealed class AssignRoleMenusRequest
{
    /// <summary>
    /// 菜单 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> MenuIds { get; init; } = Array.Empty<long>();
}

/// <summary>
/// 修改角色状态请求。
/// </summary>
public sealed class UpdateRoleStatusRequest
{
    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; }
}
