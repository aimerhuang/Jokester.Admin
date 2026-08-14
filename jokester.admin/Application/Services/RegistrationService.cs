using System.Security.Cryptography;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed class RegistrationService(
    ISqlSugarClient db,
    IPasswordHasher passwordHasher,
    IEmailValidationService emailValidationService,
    IEmailSender emailSender,
    IBlogCaptchaService captchaService,
    ILegalDocumentService legalDocumentService,
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<RedisOptions> redisOptions) : IRegistrationService
{
    private const int RegisterGiftPoints = 50;
    private const int EmailCodeRetryAfterSeconds = 60;
    private const string DefaultRegisteredUserRoleCode = "ai_operator";
    private const string DefaultRegisteredUserSiteCode = "ai_image";
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private readonly IDatabase _redis = connectionMultiplexer.GetDatabase();
    private readonly string _emailCodePrefix = $"{redisOptions.Value.InstanceName}register_email_code:";

    public async Task<SendRegisterEmailCodeResponse> SendEmailCodeAsync(
        SendRegisterEmailCodeRequest request,
        CancellationToken cancellationToken)
    {
        var email = await emailValidationService.ValidateAndNormalizeAsync(request.Email, cancellationToken);
        if (!await captchaService.ValidateAsync(request.CaptchaId, request.CaptchaAnswer, cancellationToken))
        {
            throw new AppException(ErrorCodes.BadRequest, "验证码无效或已过期");
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        await _redis.StringSetAsync(_emailCodePrefix + email, code, CodeLifetime);

        await emailSender.SendAsync(
            email,
            "Jokester registration code",
            $"Your registration verification code is {code}. It expires in 10 minutes.",
            cancellationToken);

        return new SendRegisterEmailCodeResponse { RetryAfterSeconds = EmailCodeRetryAfterSeconds };
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var userName = ValidateRegisterRequest(request);
        var email = await emailValidationService.ValidateAndNormalizeAsync(request.Email, cancellationToken);
        await ValidateEmailCodeAsync(email, request.GetEmailCode());
        await EnsureUserAvailableAsync(userName, email, cancellationToken);

        var hashed = passwordHasher.HashPassword(request.Password);
        var entity = new SysUserEntity
        {
            UserName = userName,
            NickName = string.IsNullOrWhiteSpace(request.NickName) ? userName : request.NickName.Trim(),
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

        await db.Ado.BeginTranAsync();
        try
        {
            var userId = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
            await legalDocumentService.ValidateAndRecordRegistrationConsentsAsync(userId, request, cancellationToken);
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
            await _redis.KeyDeleteAsync(_emailCodePrefix + email);
            return new RegisterResponse { UserId = userId };
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
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

    private async Task EnsureUserAvailableAsync(string userName, string email, CancellationToken cancellationToken)
    {
        var exists = await db.Queryable<SysUserEntity>()
            .AnyAsync(x => !x.IsDeleted && (x.UserName == userName || x.Email == email), cancellationToken);
        if (exists)
        {
            throw new ConflictException("User name or email already exists");
        }
    }

    private static string ValidateRegisterRequest(RegisterRequest request)
    {
        var userName = RegistrationUserNameValidator.Validate(request.UserName);

        if (request.Password.Length < 8 || request.Password.Length > 64)
        {
            throw new AppException(ErrorCodes.BadRequest, "Password length must be between 8 and 64");
        }

        return userName;
    }
}
