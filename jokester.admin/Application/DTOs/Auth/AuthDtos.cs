namespace jokester.admin.Application.DTOs.Auth;

/// <summary>
/// 登录请求。
/// </summary>
public sealed class LoginRequest
{
    /// <summary>
    /// 登录用户名。
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// 登录密码。
    /// </summary>
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// 刷新令牌请求。
/// </summary>
public sealed class RefreshTokenRequest
{
    /// <summary>
    /// RefreshToken。
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>
/// 发送注册邮箱验证码请求。
/// </summary>
public sealed class SendRegisterEmailCodeRequest
{
    /// <summary>
    /// 注册邮箱。
    /// </summary>
    public string Email { get; init; } = string.Empty;
}

/// <summary>
/// 邮箱验证码注册请求。
/// </summary>
public sealed class RegisterRequest
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
    /// 登录密码。
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// 注册邮箱。
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// 邮箱验证码。
    /// </summary>
    public string EmailCode { get; init; } = string.Empty;


    public string GetEmailCode()
    {
        return EmailCode;
    }
}

/// <summary>
/// 注册结果。
/// </summary>
public sealed class RegisterResponse
{
    /// <summary>
    /// 用户 ID。
    /// </summary>
    public long UserId { get; init; }
}

/// <summary>
/// 登录结果。
/// </summary>
public sealed class LoginResponse
{
    /// <summary>
    /// AccessToken。
    /// </summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// RefreshToken。
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>
    /// AccessToken 过期时间。
    /// </summary>
    public DateTime AccessTokenExpiresAt { get; init; }

    /// <summary>
    /// 当前用户信息。
    /// </summary>
    public UserProfileDto User { get; init; } = new();

    /// <summary>
    /// 可访问站点列表。
    /// </summary>
    public IReadOnlyCollection<SiteAccessDto> Sites { get; init; } = Array.Empty<SiteAccessDto>();

    /// <summary>
    /// 权限码列表。
    /// </summary>
    public IReadOnlyCollection<string> Permissions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 当前用户资料。
/// </summary>
public sealed class UserProfileDto
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
    /// 是否超级管理员。
    /// </summary>
    public bool IsSuperAdmin { get; init; }
}

/// <summary>
/// 可访问站点。
/// </summary>
public sealed class SiteAccessDto
{
    /// <summary>
    /// 站点 ID。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 站点编码。
    /// </summary>
    public string SiteCode { get; init; } = string.Empty;

    /// <summary>
    /// 站点名称。
    /// </summary>
    public string SiteName { get; init; } = string.Empty;
}
