using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace jokester.admin.Controllers;

[Route("api/auth")]
public sealed class AuthController(
    IAuthService authService,
    IRegistrationService registrationService,
    IAccountDeletionService accountDeletionService) : BaseApiController
{
    /// <summary>
    /// 发送注册邮箱验证码。
    /// </summary>
    /// <remarks>
    /// 请求体仅传 email；发送频率由服务端按邮箱和 IP 限制。
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting("AuthAbuseProtection")]
    [RequestSizeLimit(1 * 1024 * 1024)]
    [HttpPost("register/email-code")]
    [ProducesResponseType(typeof(ApiResponse<SendRegisterEmailCodeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendRegisterEmailCode(
        [FromBody] SendRegisterEmailCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.SendEmailCodeAsync(request, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 使用邮箱、邮箱验证码和密码注册；账号与昵称由服务端根据邮箱自动生成。
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("AuthAbuseProtection")]
    [RequestSizeLimit(1 * 1024 * 1024)]
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status200OK)]
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
    [EnableRateLimiting("AuthAbuseProtection")]
    [RequestSizeLimit(1 * 1024 * 1024)]
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await authService.LoginAsync(request, cancellationToken);
            return Success(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await authService.RecordLoginFailureAsync(request.UserName, "登录失败", cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 刷新访问令牌。
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("AuthAbuseProtection")]
    [RequestSizeLimit(1 * 1024 * 1024)]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 用户登出。
    /// </summary>
    /// <remarks>
    /// RefreshToken 仅通过 X-Refresh-Token 请求头传入，避免出现在 URL 和访问日志中。
    /// </remarks>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Headers["X-Refresh-Token"].FirstOrDefault();
        await authService.LogoutAsync(refreshToken, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 撤销当前用户的全部登录会话。
    /// </summary>
    [Authorize]
    [HttpPost("logout-all")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        await authService.LogoutAllAsync(cancellationToken);
        return Success();
    }

    /// <summary>
    /// 获取当前登录用户信息。
    /// </summary>
    [Authorize]
    [HttpGet("profile")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var result = await authService.GetProfileAsync(cancellationToken);
        return Success(result);
    }

    [Authorize]
    [HttpPost("account-deletion/requests")]
    [ProducesResponseType(typeof(ApiResponse<AccountDeletionRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestAccountDeletion(
        [FromBody] CreateAccountDeletionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountDeletionService.CreateAsync(request, cancellationToken);
        return Success(result);
    }

    [Authorize]
    [HttpGet("account-deletion/requests/current")]
    [ProducesResponseType(typeof(ApiResponse<AccountDeletionRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccountDeletionRequest(CancellationToken cancellationToken)
    {
        var result = await accountDeletionService.GetCurrentAsync(cancellationToken);
        return Success(result);
    }

    [Authorize]
    [HttpDelete("account-deletion/requests/{requestId}")]
    [ProducesResponseType(typeof(ApiResponse<AccountDeletionRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelAccountDeletion(
        string requestId,
        CancellationToken cancellationToken)
    {
        var result = await accountDeletionService.CancelAsync(requestId, cancellationToken);
        return Success(result);
    }
}
