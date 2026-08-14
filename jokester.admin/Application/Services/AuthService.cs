using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed class AuthService(
    ISqlSugarClient db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IRefreshTokenStore refreshTokenStore,
    IPermissionService permissionService,
    ICurrentUser currentUser,
    IAuditLogWriter auditLogWriter,
    IHttpContextAccessor httpContextAccessor,
    IBlogCaptchaService captchaService,
    IConnectionMultiplexer connectionMultiplexer,
    IAppleAppAccountTokenService appleAccountIdService,
    IOptions<AppleAppStoreOptions> appleOptions,
    IOptions<JwtOptions> jwtOptions) : IAuthService
{
    private static readonly TimeSpan LoginWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LoginLockDuration = TimeSpan.FromMinutes(15);
    private readonly IDatabase _redis = connectionMultiplexer.GetDatabase();
    private readonly string _loginFailPrefix = $"{jwtOptions.Value.Issuer}:login_fail:";
    private readonly string _loginLockPrefix = $"{jwtOptions.Value.Issuer}:login_lock:";

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var userName = NormalizeUserName(request.UserName);
        var ip = GetClientIp();
        var failKey = BuildLoginFailKey(userName, ip);
        var lockKey = BuildLoginLockKey(userName, ip);

        if (await _redis.KeyExistsAsync(lockKey))
        {
            throw new AppException(ErrorCodes.LoginLocked, "账号已锁定，请 15 分钟后重试");
        }

        var attempts = await GetFailedAttemptsAsync(failKey);
        if (attempts >= 3 && !await captchaService.ValidateAsync(request.CaptchaId ?? string.Empty, request.CaptchaAnswer ?? string.Empty, cancellationToken))
        {
            throw new AppException(ErrorCodes.CaptchaRequired, "请先完成验证码验证");
        }

        var user = await db.Queryable<SysUserEntity>()
            .FirstAsync(x => x.UserName == request.UserName && !x.IsDeleted, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash, user.Salt))
        {
            attempts++;
            await RecordLoginFailureAsync(failKey, lockKey, attempts, request.UserName, "用户名或密码错误", cancellationToken);
            throw new AppException(ErrorCodes.InvalidCredentials, "用户名或密码错误");
        }

        if (user.Status != 1)
        {
            throw new AppException(ErrorCodes.InvalidCredentials, "用户名或密码错误");
        }

        await ResetLoginFailuresAsync(failKey, lockKey, cancellationToken);
        var response = await BuildLoginResponseAsync(user, cancellationToken);
        await RecordSuccessfulLoginAsync(user, cancellationToken);
        return response;
    }

    public async Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw RefreshTokenExpired();
        }

        var consumed = await refreshTokenStore.ConsumeAsync(request.RefreshToken, cancellationToken);
        if (consumed.Status == RefreshTokenConsumeStatus.Invalid)
        {
            throw RefreshTokenExpired();
        }
        if (consumed.Status is RefreshTokenConsumeStatus.Replayed or RefreshTokenConsumeStatus.Revoked
            || !consumed.UserId.HasValue
            || string.IsNullOrWhiteSpace(consumed.SessionId))
        {
            throw SessionRevoked();
        }

        var user = await db.Queryable<SysUserEntity>()
            .FirstAsync(x => x.Id == consumed.UserId.Value && !x.IsDeleted, cancellationToken);

        if (user is null || user.Status != 1)
        {
            await refreshTokenStore.RevokeUserSessionsAsync(consumed.UserId.Value, cancellationToken);
            throw SessionRevoked();
        }

        if (consumed.Status == RefreshTokenConsumeStatus.Concurrent)
        {
            if (consumed.Tokens is null)
            {
                throw new AppException(
                    ErrorCodes.ServiceUnavailable,
                    MachineErrorCodes.ServiceUnavailable,
                    "Refresh token rotation is still in progress.");
            }
            return await BuildLoginResponseAsync(user, cancellationToken, consumed.SessionId, consumed.Tokens);
        }

        var response = await BuildLoginResponseAsync(user, cancellationToken, consumed.SessionId);
        var completed = await refreshTokenStore.CompleteRotationAsync(
            request.RefreshToken,
            response.RefreshToken,
            new RefreshTokenRotationTokens(
                response.AccessToken,
                response.RefreshToken,
                response.AccessTokenExpiresAt,
                response.RefreshTokenExpiresAt),
            cancellationToken);
        if (!completed)
        {
            await refreshTokenStore.RevokeAsync(response.RefreshToken, CancellationToken.None);
            throw SessionRevoked();
        }
        return response;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await refreshTokenStore.RevokeAsync(refreshToken, cancellationToken);
        }
    }

    public async Task LogoutAllAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
        {
            throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "User is not authenticated.");
        }
        await refreshTokenStore.RevokeUserSessionsAsync(currentUser.UserId.Value, cancellationToken);
    }

    public async Task<UserProfileDto> GetProfileAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
        {
            throw new AppException(ErrorCodes.Unauthorized, "未登录");
        }

        var user = await db.Queryable<SysUserEntity>()
            .Where(x => x.Id == currentUser.UserId.Value && !x.IsDeleted)
            .Select(x => new UserProfileDto
            {
                Id = x.Id,
                UserName = x.UserName,
                NickName = x.NickName ?? string.Empty,
                Email = x.Email,
                AvatarUrl = x.AvatarUrl,
                Signature = x.Signature,
                PointBalance = x.PointBalance,
                IsSuperAdmin = x.IsSuperAdmin
            })
            .FirstAsync(cancellationToken);

        if (user is null)
        {
            throw new AppException(ErrorCodes.Unauthorized, "未登录");
        }

        if (!appleOptions.Value.Enabled) return user;

        return new UserProfileDto
        {
            Id = user.Id,
            UserName = user.UserName,
            NickName = user.NickName,
            Email = user.Email,
            AvatarUrl = user.AvatarUrl,
            Signature = user.Signature,
            PointBalance = user.PointBalance,
            IsSuperAdmin = user.IsSuperAdmin,
            AppleAppAccountToken = appleAccountIdService.GetForUser(user.Id)
        };
    }

    public async Task RecordLoginFailureAsync(string? userName, string errorMessage, CancellationToken cancellationToken)
    {
        await auditLogWriter.WriteLoginAsync(null, userName, false, errorMessage, cancellationToken);
    }

    private async Task<LoginResponse> BuildLoginResponseAsync(
        SysUserEntity user,
        CancellationToken cancellationToken,
        string? existingSessionId = null)
    {
        var sessionId = existingSessionId ?? Guid.NewGuid().ToString("N");
        var accessToken = tokenService.CreateAccessToken(user.Id, user.UserName, user.IsSuperAdmin, sessionId);
        var refreshToken = tokenService.CreateRefreshToken();
        var accessTokenExpiresAt = tokenService.GetAccessTokenExpiresAt();
        var refreshExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenExpiresDays);

        var sites = await db.Queryable<SysUserSiteEntity, SysSiteEntity>((us, s) => new JoinQueryInfos(JoinType.Inner, us.SiteId == s.Id))
            .Where((us, s) => us.UserId == user.Id && !s.IsDeleted && s.Status == 1)
            .OrderBy((us, s) => s.Sort)
            .Select((us, s) => new SiteAccessDto
            {
                Id = s.Id,
                SiteCode = s.SiteCode,
                SiteName = s.SiteName
            })
            .ToListAsync(cancellationToken);
        var permissions = await permissionService.GetPermissionsAsync(user.Id, user.IsSuperAdmin, cancellationToken);
        var saved = await refreshTokenStore.SaveAsync(refreshToken, user.Id, sessionId, refreshExpiresAt, cancellationToken);
        if (!saved)
        {
            if (existingSessionId is not null) throw SessionRevoked();

            throw new AppException(
                ErrorCodes.ServiceUnavailable,
                MachineErrorCodes.ServiceUnavailable,
                "Login session storage is temporarily unavailable.");
        }

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = accessTokenExpiresAt,
            RefreshTokenExpiresAt = refreshExpiresAt,
            SessionId = sessionId,
            User = new UserProfileDto
            {
                Id = user.Id,
                UserName = user.UserName,
                NickName = user.NickName ?? string.Empty,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                Signature = user.Signature,
                PointBalance = user.PointBalance,
                IsSuperAdmin = user.IsSuperAdmin,
                AppleAppAccountToken = appleOptions.Value.Enabled
                    ? appleAccountIdService.GetForUser(user.Id)
                    : null
            },
            Sites = sites,
            Permissions = permissions
        };
    }

    private async Task<LoginResponse> BuildLoginResponseAsync(
        SysUserEntity user,
        CancellationToken cancellationToken,
        string sessionId,
        RefreshTokenRotationTokens tokens)
    {
        var sites = await db.Queryable<SysUserSiteEntity, SysSiteEntity>((us, s) => new JoinQueryInfos(JoinType.Inner, us.SiteId == s.Id))
            .Where((us, s) => us.UserId == user.Id && !s.IsDeleted && s.Status == 1)
            .OrderBy((us, s) => s.Sort)
            .Select((us, s) => new SiteAccessDto
            {
                Id = s.Id,
                SiteCode = s.SiteCode,
                SiteName = s.SiteName
            })
            .ToListAsync(cancellationToken);
        var permissions = await permissionService.GetPermissionsAsync(user.Id, user.IsSuperAdmin, cancellationToken);
        return new LoginResponse
        {
            SessionId = sessionId,
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            AccessTokenExpiresAt = tokens.AccessTokenExpiresAt,
            RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt,
            User = new UserProfileDto
            {
                Id = user.Id,
                UserName = user.UserName,
                NickName = user.NickName ?? string.Empty,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                Signature = user.Signature,
                PointBalance = user.PointBalance,
                IsSuperAdmin = user.IsSuperAdmin,
                AppleAppAccountToken = appleOptions.Value.Enabled
                    ? appleAccountIdService.GetForUser(user.Id)
                    : null
            },
            Sites = sites,
            Permissions = permissions
        };
    }

    private static AppException RefreshTokenExpired() => new(
        ErrorCodes.Unauthorized,
        MachineErrorCodes.RefreshTokenExpired,
        "Refresh token is missing or expired.");

    private static AppException SessionRevoked() => new(
        ErrorCodes.Unauthorized,
        MachineErrorCodes.SessionRevoked,
        "The login session has been revoked.");

    private async Task RecordSuccessfulLoginAsync(SysUserEntity user, CancellationToken cancellationToken)
    {
        var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity
            {
                LastLoginTime = DateTime.Now,
                LastLoginIp = ip,
                UpdatedAt = DateTime.Now
            })
            .Where(x => x.Id == user.Id)
            .ExecuteCommandAsync();

        await auditLogWriter.WriteLoginAsync(user.Id, user.UserName, true, null, cancellationToken);
    }

    private async Task RecordLoginFailureAsync(string failKey, string lockKey, int attempts, string userName, string errorMessage, CancellationToken cancellationToken)
    {
        await _redis.StringSetAsync(failKey, attempts.ToString(), LoginWindow);
        if (attempts >= 5)
        {
            await _redis.StringSetAsync(lockKey, "1", LoginLockDuration);
            throw new AppException(ErrorCodes.LoginLocked, "账号已锁定，请 15 分钟后重试");
        }

        await auditLogWriter.WriteLoginAsync(null, userName, false, errorMessage, cancellationToken);
    }

    private async Task ResetLoginFailuresAsync(string failKey, string lockKey, CancellationToken cancellationToken)
    {
        await _redis.KeyDeleteAsync(failKey);
        await _redis.KeyDeleteAsync(lockKey);
    }

    private async Task<int> GetFailedAttemptsAsync(string failKey)
    {
        var value = await _redis.StringGetAsync(failKey);
        return value.HasValue && int.TryParse(value.ToString(), out var attempts) ? attempts : 0;
    }

    private string GetClientIp() => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string NormalizeUserName(string userName) => string.IsNullOrWhiteSpace(userName) ? string.Empty : userName.Trim().ToLowerInvariant();

    private string BuildLoginFailKey(string userName, string ip) => $"{_loginFailPrefix}{userName}:{ip}";

    private string BuildLoginLockKey(string userName, string ip) => $"{_loginLockPrefix}{userName}:{ip}";
}
