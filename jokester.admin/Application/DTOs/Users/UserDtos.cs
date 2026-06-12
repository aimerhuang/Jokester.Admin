using jokester.admin.Application.DTOs.Common;

namespace jokester.admin.Application.DTOs.Users;

/// <summary>
/// 用户授权菜单树节点。
/// </summary>
public sealed class UserPermissionTreeNodeDto
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
    /// 当前用户是否已拥有该节点。
    /// </summary>
    public bool Checked { get; init; }

    /// <summary>
    /// 是否禁用勾选。
    /// </summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// 子菜单列表。
    /// </summary>
    public IReadOnlyCollection<UserPermissionTreeNodeDto> Children { get; init; } = Array.Empty<UserPermissionTreeNodeDto>();
}

/// <summary>
/// 用户授权菜单树。
/// </summary>
public sealed class UserPermissionTreeDto
{
    /// <summary>
    /// 用户 ID。
    /// </summary>
    public long UserId { get; init; }

    /// <summary>
    /// 用户当前角色 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> RoleIds { get; init; } = Array.Empty<long>();

    /// <summary>
    /// 用户当前已拥有菜单 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> GrantedMenuIds { get; init; } = Array.Empty<long>();

    /// <summary>
    /// 授权菜单树。
    /// </summary>
    public IReadOnlyCollection<UserPermissionTreeNodeDto> Tree { get; init; } = Array.Empty<UserPermissionTreeNodeDto>();
}

/// <summary>
/// 分配用户菜单权限请求。
/// </summary>
public sealed class AssignUserMenusRequest
{
    /// <summary>
    /// 菜单 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> MenuIds { get; init; } = Array.Empty<long>();
}

/// <summary>
/// 用户列表项。
/// </summary>
public sealed class UserListItemDto
{
    /// <summary>
    /// 用户 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 登录用户名。
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// 用户昵称。
    /// </summary>
    public string NickName { get; init; } = string.Empty;

    /// <summary>
    /// 邮箱。
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// 手机号。
    /// </summary>
    public string? Phone { get; init; }

    /// <summary>
    /// 头像地址。
    /// </summary>
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// 个性签名。
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// 当前积分余额。
    /// </summary>
    public int PointBalance { get; init; }

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; }

    /// <summary>
    /// 是否超级管理员。
    /// </summary>
    public bool IsSuperAdmin { get; init; }

    /// <summary>
    /// 角色 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> RoleIds { get; init; } = Array.Empty<long>();

    /// <summary>
    /// 站点 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> SiteIds { get; init; } = Array.Empty<long>();
}

/// <summary>
/// 新增用户请求。
/// </summary>
public sealed class SaveUserRequest
{
    /// <summary>
    /// 登录用户名。
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// 用户昵称。
    /// </summary>
    public string NickName { get; init; } = string.Empty;

    /// <summary>
    /// 明文密码。
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// 邮箱。
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// 手机号。
    /// </summary>
    public string? Phone { get; init; }

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; } = 1;

    /// <summary>
    /// 头像地址。
    /// </summary>
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; init; }

    /// <summary>
    /// 是否超级管理员。
    /// </summary>
    public bool IsSuperAdmin { get; init; }

    /// <summary>
    /// 角色 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> RoleIds { get; init; } = Array.Empty<long>();

    /// <summary>
    /// 站点 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> SiteIds { get; init; } = Array.Empty<long>();
}

/// <summary>
/// 编辑用户基础信息请求。
/// </summary>
public sealed class UpdateUserInfoRequest
{
    /// <summary>
    /// 用户昵称。
    /// </summary>
    public string NickName { get; init; } = string.Empty;

    /// <summary>
    /// 邮箱。
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// 手机号。
    /// </summary>
    public string? Phone { get; init; }

    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; } = 1;

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; init; }

    /// <summary>
    /// 是否超级管理员。
    /// </summary>
    public bool IsSuperAdmin { get; init; }

    /// <summary>
    /// 角色 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> RoleIds { get; init; } = Array.Empty<long>();

    /// <summary>
    /// 站点 ID 列表。
    /// </summary>
    public IReadOnlyCollection<long> SiteIds { get; init; } = Array.Empty<long>();
}

/// <summary>
/// 修改用户昵称请求。
/// </summary>
public sealed class UpdateUserNickNameRequest
{
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// 用户昵称。
    /// </summary>
    public string NickName { get; init; } = string.Empty;
}

/// <summary>
/// 修改用户密码请求。
/// </summary>
public sealed class UpdateUserPasswordRequest
{
    /// <summary>
    /// 新密码。
    /// </summary>
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// 上传用户头像请求。
/// </summary>
public sealed class UploadUserAvatarRequest
{
    /// <summary>
    /// 头像文件。
    /// </summary>
    public IFormFile? File { get; init; }
}

/// <summary>
/// 修改用户个性签名请求。
/// </summary>
public sealed class UpdateUserSignatureRequest
{
    /// <summary>
    /// 个性签名。
    /// </summary>
    public string? Signature { get; init; }
}

/// <summary>
/// 修改用户状态请求。
/// </summary>
public sealed class UpdateUserStatusRequest
{
    /// <summary>
    /// 状态：1=启用，0=禁用。
    /// </summary>
    public int Status { get; init; }
}

/// <summary>
/// 用户积分明细。
/// </summary>
public sealed class UserPointDetailDto
{
    /// <summary>
    /// 明细 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 用户 ID。
    /// </summary>
    public long UserId { get; init; }

    /// <summary>
    /// 积分变动值，正数增加，负数扣减。
    /// </summary>
    public int ChangePoints { get; init; }

    /// <summary>
    /// 变动后积分余额。
    /// </summary>
    public int BalanceAfter { get; init; }

    /// <summary>
    /// 变动类型。
    /// </summary>
    public string ChangeType { get; init; } = string.Empty;

    /// <summary>
    /// 来源。
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; init; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
