using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Route("api/auth")]
public sealed class AuthController(
    IAuthService authService,
    IRegistrationService registrationService) : BaseApiController
{
    /// <summary>
    /// 发送注册邮箱验证码。
    /// </summary>
    /// <remarks>
    /// 请求体只需要传 email；验证码用于邮箱注册。
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("register/email-code")]
    public async Task<IActionResult> SendRegisterEmailCode(
        [FromBody] SendRegisterEmailCodeRequest request,
        CancellationToken cancellationToken)
    {
        await registrationService.SendEmailCodeAsync(request, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 邮箱验证码注册。
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await registrationService.RegisterAsync(request, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 用户登录。
    /// </summary>
    /// <remarks>
    /// 登录成功返回 AccessToken、RefreshToken、用户信息、可访问站点和权限码。
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await authService.LoginAsync(request, cancellationToken);
            return Success(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await authService.RecordLoginFailureAsync(request.UserName, ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 刷新访问令牌。
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 用户登出。
    /// </summary>
    /// <remarks>
    /// RefreshToken 可通过 X-Refresh-Token 请求头或 refreshToken 查询参数传入。
    /// </remarks>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Headers["X-Refresh-Token"].FirstOrDefault()
            ?? Request.Query["refreshToken"].FirstOrDefault();
        await authService.LogoutAsync(refreshToken, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 获取当前登录用户信息。
    /// </summary>
    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var result = await authService.GetProfileAsync(cancellationToken);
        return Success(result);
    }
}
