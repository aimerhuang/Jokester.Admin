using System.Security.Cryptography;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using MySqlConnector;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed class RegistrationService(
    ISqlSugarClient db,
    IPasswordHasher passwordHasher,
    IEmailValidationService emailValidationService,
    IEmailSender emailSender,
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<RedisOptions> redisOptions,
    ILogger<RegistrationService> logger) : IRegistrationService
{
    private const int RegisterGiftPoints = 50;
    private const int EmailCodeRetryAfterSeconds = 60;
    private const string DefaultRegisteredUserRoleCode = "ai_operator";
    private const string DefaultRegisteredUserSiteCode = "ai_image";
    private const string DeleteEmailCodeIfMatchesScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        end
        return 0
        """;
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private readonly IDatabase _redis = connectionMultiplexer.GetDatabase();
    private readonly string _emailCodePrefix = $"{redisOptions.Value.InstanceName}register_email_code:";

    public async Task<SendRegisterEmailCodeResponse> SendEmailCodeAsync(
        SendRegisterEmailCodeRequest request,
        CancellationToken cancellationToken)
    {
        var email = await emailValidationService.ValidateAndNormalizeAsync(request.Email, cancellationToken);
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var emailCodeKey = _emailCodePrefix + email;
        await _redis.StringSetAsync(emailCodeKey, code, CodeLifetime);

        try
        {
            await emailSender.SendAsync(
                email,
                "Jokester registration code",
                $"Your registration verification code is {code}. It expires in 10 minutes.",
                cancellationToken);
        }
        catch
        {
            try
            {
                await _redis.ScriptEvaluateAsync(
                    DeleteEmailCodeIfMatchesScript,
                    [emailCodeKey],
                    [code]);
            }
            catch (Exception cleanupException)
            {
                logger.LogWarning(
                    cleanupException,
                    "Failed to clean up a registration email code after email delivery failed.");
            }

            throw;
        }

        return new SendRegisterEmailCodeResponse { RetryAfterSeconds = EmailCodeRetryAfterSeconds };
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = await emailValidationService.ValidateAndNormalizeAsync(request.Email, cancellationToken);
        ValidatePassword(request.Password);
        await ValidateEmailCodeAsync(email, request.EmailCode);
        var identity = await ResolveIdentityAsync(email, cancellationToken);

        var hashed = passwordHasher.HashPassword(request.Password);
        var entity = new SysUserEntity
        {
            UserName = identity.UserName,
            NickName = identity.NickName,
            PasswordHash = hashed.Hash,
            Salt = hashed.Salt,
            Email = email,
            PointBalance = RegisterGiftPoints,
            Status = 1,
            IsSuperAdmin = false,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDeleted = false
        };

        long userId;
        await db.Ado.BeginTranAsync();
        try
        {
            userId = await InsertUserWithCollisionRetryAsync(entity, email, cancellationToken);
            await AssignDefaultAiImageAccessAsync(userId, cancellationToken);
            await db.Insertable(new UserPointDetailEntity
            {
                UserId = userId,
                ChangePoints = RegisterGiftPoints,
                BalanceAfter = RegisterGiftPoints,
                ChangeType = "gift",
                Source = "register",
                BusinessKey = $"register:user:{userId}",
                Remark = "注册赠送积分",
                CreatedAt = DateTime.Now
            }).ExecuteCommandAsync(cancellationToken);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        try
        {
            await _redis.ScriptEvaluateAsync(
                DeleteEmailCodeIfMatchesScript,
                [_emailCodePrefix + email],
                [request.EmailCode.Trim()]);
        }
        catch (Exception cleanupException)
        {
            logger.LogWarning(
                cleanupException,
                "Registration succeeded, but its email code could not be removed.");
        }

        return new RegisterResponse { UserId = userId };
    }

    private async Task AssignDefaultAiImageAccessAsync(long userId, CancellationToken cancellationToken)
    {
        var role = await db.Queryable<SysRoleEntity>()
            .FirstAsync(x => x.RoleCode == DefaultRegisteredUserRoleCode && x.Status == 1 && !x.IsDeleted, cancellationToken);
        if (role is null)
        {
            throw new AppException(ErrorCodes.BadRequest, "默认 AI Image 角色未配置或已禁用");
        }

        var site = await db.Queryable<SysSiteEntity>()
            .FirstAsync(x => x.SiteCode == DefaultRegisteredUserSiteCode && x.Status == 1 && !x.IsDeleted, cancellationToken);
        if (site is null)
        {
            throw new AppException(ErrorCodes.BadRequest, "默认 AI Image 站点未配置或已禁用");
        }

        await db.Insertable(new SysUserRoleEntity
        {
            UserId = userId,
            RoleId = role.Id,
            CreatedAt = DateTime.Now
        }).ExecuteCommandAsync(cancellationToken);

        await db.Insertable(new SysUserSiteEntity
        {
            UserId = userId,
            SiteId = site.Id,
            CreatedAt = DateTime.Now
        }).ExecuteCommandAsync(cancellationToken);
    }

    private async Task ValidateEmailCodeAsync(string email, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new AppException(ErrorCodes.BadRequest, "Email code is required");
        }

        var stored = await _redis.StringGetAsync(_emailCodePrefix + email);
        if (!stored.HasValue || !string.Equals(stored.ToString(), code.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid email code");
        }
    }

    private async Task<long> InsertUserWithCollisionRetryAsync(
        SysUserEntity entity,
        string email,
        CancellationToken cancellationToken)
    {
        try
        {
            return await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
        }
        catch (Exception exception) when (IsDuplicateKey(exception))
        {
            if (await db.Queryable<SysUserEntity>().AnyAsync(x => x.Email == email, cancellationToken))
            {
                throw new ConflictException("Email already exists");
            }

            var defaultUserName = RegistrationIdentityFactory.Create(email).UserName;
            if (!string.Equals(entity.UserName, defaultUserName, StringComparison.Ordinal))
            {
                throw new ConflictException("Unable to assign an account name for this email");
            }

            entity.UserName = RegistrationIdentityFactory.CreateDisambiguatedUserName(email);
            try
            {
                return await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
            }
            catch (Exception retryException) when (IsDuplicateKey(retryException))
            {
                if (await db.Queryable<SysUserEntity>().AnyAsync(x => x.Email == email, cancellationToken))
                {
                    throw new ConflictException("Email already exists");
                }

                throw new ConflictException("Unable to assign an account name for this email");
            }
        }
    }

    private async Task<RegistrationIdentity> ResolveIdentityAsync(string email, CancellationToken cancellationToken)
    {
        if (await db.Queryable<SysUserEntity>().AnyAsync(x => x.Email == email, cancellationToken))
        {
            throw new ConflictException("Email already exists");
        }

        var identity = RegistrationIdentityFactory.Create(email);
        if (!await db.Queryable<SysUserEntity>().AnyAsync(x => x.UserName == identity.UserName, cancellationToken))
        {
            return identity;
        }

        var disambiguated = RegistrationIdentityFactory.CreateDisambiguatedUserName(email);
        if (await db.Queryable<SysUserEntity>().AnyAsync(x => x.UserName == disambiguated, cancellationToken))
        {
            throw new ConflictException("Unable to assign an account name for this email");
        }

        return identity with { UserName = disambiguated };
    }

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length is < 8 or > 64)
        {
            throw new AppException(ErrorCodes.BadRequest, "Password length must be between 8 and 64");
        }
    }

    private static bool IsDuplicateKey(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException { Number: 1062 }) return true;
        }

        return false;
    }
}
