using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;

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
    IOptions<JwtOptions> jwtOptions) : IAuthService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Queryable<SysUserEntity>()
            .FirstAsync(x => x.UserName == request.UserName && !x.IsDeleted, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash, user.Salt))
        {
            throw new AppException(ErrorCodes.InvalidCredentials, "用户名或密码错误");
        }

        if (user.Status != 1)
        {
            throw new AppException(ErrorCodes.InvalidCredentials, "用户名或密码错误");
        }

        var response = await BuildLoginResponseAsync(user, cancellationToken);
        await RecordSuccessfulLoginAsync(user, cancellationToken);
        return response;
    }

    public async Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var consumed = await refreshTokenStore.ConsumeAsync(request.RefreshToken, cancellationToken);
        if (consumed.Status != RefreshTokenConsumeStatus.Succeeded
            || !consumed.UserId.HasValue
            || string.IsNullOrWhiteSpace(consumed.SessionId))
        {
            throw new AppException(ErrorCodes.InvalidRefreshToken, "RefreshToken 无效或已过期");
        }

        var user = await db.Queryable<SysUserEntity>()
            .FirstAsync(x => x.Id == consumed.UserId.Value && !x.IsDeleted, cancellationToken);

        if (user is null || user.Status != 1)
        {
            throw new AppException(ErrorCodes.InvalidRefreshToken, "RefreshToken 无效或已过期");
        }

        return await BuildLoginResponseAsync(user, cancellationToken, consumed.SessionId);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await refreshTokenStore.RevokeAsync(refreshToken, cancellationToken);
        }
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

        return user;
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
            throw new AppException(ErrorCodes.InvalidRefreshToken, "RefreshToken 会话已失效，请重新登录");
        }

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = accessTokenExpiresAt,
            User = new UserProfileDto
            {
                Id = user.Id,
                UserName = user.UserName,
                NickName = user.NickName ?? string.Empty,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                Signature = user.Signature,
                PointBalance = user.PointBalance,
                IsSuperAdmin = user.IsSuperAdmin
            },
            Sites = sites,
            Permissions = permissions
        };
    }

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
}
