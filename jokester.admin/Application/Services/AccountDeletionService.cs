using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AccountDeletionService(
    ISqlSugarClient db,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    IRefreshTokenStore refreshTokenStore,
    IAiMediaPathResolver mediaPathResolver,
    IWebHostEnvironment environment,
    IEmailSender emailSender,
    ILogger<AccountDeletionService> logger) : IAccountDeletionService
{
    private const string ScheduledStatus = "scheduled";
    private const string ProcessingStatus = "processing";
    private const string NotificationPendingStatus = "notification_pending";
    private static readonly TimeSpan GracePeriod = TimeSpan.FromDays(7);
    private static readonly TimeSpan RecentLoginWindow = TimeSpan.FromMinutes(30);

    public async Task<AccountDeletionRequestDto> CreateAsync(
        CreateAccountDeletionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUser();
        ValidateCreateRequest(request);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var clientRequestHash = Hash(request.ClientRequestId.Trim());
        var fingerprint = Hash(JsonSerializer.Serialize(new { confirmation = "DELETE", reason }));
        var existing = await db.Queryable<AccountDeletionRequestEntity>()
            .FirstAsync(x => x.UserId == userId && x.ClientRequestHash == clientRequestHash, cancellationToken);
        if (existing is not null)
        {
            EnsureMatchingFingerprint(existing, fingerprint);
            if (existing.Status is ScheduledStatus or ProcessingStatus or NotificationPendingStatus or "failed")
            {
                await refreshTokenStore.RevokeUserSessionsAsync(userId, cancellationToken);
            }
            return Map(existing);
        }

        var now = DateTime.UtcNow;
        AccountDeletionRequestEntity result;
        await db.Ado.BeginTranAsync();
        try
        {
            var user = await db.Queryable<SysUserEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken)
                ?? throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "User is not authenticated.");

            var concurrentIdempotent = await db.Queryable<AccountDeletionRequestEntity>()
                .FirstAsync(x => x.UserId == userId && x.ClientRequestHash == clientRequestHash, cancellationToken);
            if (concurrentIdempotent is not null)
            {
                EnsureMatchingFingerprint(concurrentIdempotent, fingerprint);
                result = concurrentIdempotent;
            }
            else
            {
                if (user.LastLoginTime is null || DateTime.Now - user.LastLoginTime.Value > RecentLoginWindow)
                {
                    throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.ReauthenticationRequired, "A recent login is required before deleting the account.");
                }
                if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash, user.Salt))
                {
                    throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.ReauthenticationRequired, "The current password is invalid.");
                }

                var active = await db.Queryable<AccountDeletionRequestEntity>()
                    .FirstAsync(x => x.UserId == userId
                        && (x.Status == ScheduledStatus || x.Status == ProcessingStatus || x.Status == NotificationPendingStatus), cancellationToken);
                if (active is not null)
                {
                    result = active;
                }
                else
                {
                    result = new AccountDeletionRequestEntity
                    {
                        RequestId = CreateRequestId(now),
                        UserId = userId,
                        ClientRequestHash = clientRequestHash,
                        RequestFingerprint = fingerprint,
                        Status = ScheduledStatus,
                        Reason = reason,
                        NotificationEmail = user.Email,
                        RequestedAt = now,
                        ScheduledDeletionAt = now.Add(GracePeriod),
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    await db.Insertable(result).ExecuteCommandAsync(cancellationToken);
                }
            }
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        await refreshTokenStore.RevokeUserSessionsAsync(userId, cancellationToken);
        return Map(result);
    }

    public async Task<AccountDeletionRequestDto?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUser();
        var entity = await db.Queryable<AccountDeletionRequestEntity>()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .OrderByDescending(x => x.Id)
            .FirstAsync(cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<AccountDeletionRequestDto> CancelAsync(string requestId, CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUser();
        var normalizedId = string.IsNullOrWhiteSpace(requestId) ? string.Empty : requestId.Trim();
        var entity = await db.Queryable<AccountDeletionRequestEntity>()
            .FirstAsync(x => x.UserId == userId && x.RequestId == normalizedId, cancellationToken)
            ?? throw new AppException(ErrorCodes.NotFound, MachineErrorCodes.ResourceNotFound, "Account deletion request was not found.");
        if (entity.Status != ScheduledStatus || entity.ScheduledDeletionAt <= DateTime.UtcNow)
        {
            throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.Conflict, "The account deletion request can no longer be cancelled.");
        }

        var now = DateTime.UtcNow;
        await db.Ado.BeginTranAsync();
        try
        {
            var updated = await db.Updateable<AccountDeletionRequestEntity>()
                .SetColumns(x => new AccountDeletionRequestEntity
                {
                    Status = "cancelled",
                    CancelledAt = now,
                    NotificationEmail = null,
                    UpdatedAt = now
                })
                .Where(x => x.Id == entity.Id && x.Status == ScheduledStatus)
                .ExecuteCommandAsync(cancellationToken);
            if (updated != 1)
            {
                throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.Conflict, "The account deletion request can no longer be cancelled.");
            }
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        entity.Status = "cancelled";
        entity.CancelledAt = now;
        entity.NotificationEmail = null;
        entity.UpdatedAt = now;
        return Map(entity);
    }

    public async Task ProcessDueRequestsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var staleProcessingBefore = now.AddMinutes(-15);
        var requests = await db.Queryable<AccountDeletionRequestEntity>()
            .Where(x => (x.Status == ScheduledStatus && x.ScheduledDeletionAt <= now)
                || (x.Status == "failed" && x.NextRetryAt <= now)
                || (x.Status == ProcessingStatus && x.UpdatedAt <= staleProcessingBefore)
                || (x.Status == NotificationPendingStatus
                    && (x.NextRetryAt == null || x.NextRetryAt <= now)))
            .OrderBy(x => x.ScheduledDeletionAt)
            .Take(10)
            .ToListAsync(cancellationToken);
        foreach (var request in requests)
        {
            await ProcessRequestAsync(request, cancellationToken);
        }
    }

    private async Task ProcessRequestAsync(AccountDeletionRequestEntity request, CancellationToken cancellationToken)
    {
        try
        {
            if (!request.DataDeletedAt.HasValue)
            {
                var staleProcessingBefore = DateTime.UtcNow.AddMinutes(-15);
                var claimed = await db.Updateable<AccountDeletionRequestEntity>()
                    .SetColumns(x => new AccountDeletionRequestEntity { Status = ProcessingStatus, UpdatedAt = DateTime.UtcNow })
                    .Where(x => x.Id == request.Id
                        && (x.Status == ScheduledStatus
                            || x.Status == "failed"
                            || (x.Status == ProcessingStatus && x.UpdatedAt <= staleProcessingBefore)))
                    .ExecuteCommandAsync(cancellationToken);
                if (claimed != 1) return;
                await DeleteUserDataAsync(request, cancellationToken);
                request.DataDeletedAt = DateTime.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(request.NotificationEmail))
            {
                await emailSender.SendAsync(
                    request.NotificationEmail,
                    "Jokester account deletion completed",
                    "Your Jokester account and associated private AI data have been deleted.",
                    cancellationToken);
            }

            var completedAt = DateTime.UtcNow;
            await db.Updateable<AccountDeletionRequestEntity>()
                .SetColumns(x => new AccountDeletionRequestEntity
                {
                    Status = "completed",
                    DataDeletedAt = request.DataDeletedAt ?? completedAt,
                    CompletedAt = completedAt,
                    NotificationSentAt = string.IsNullOrWhiteSpace(request.NotificationEmail) ? null : completedAt,
                    NotificationEmail = null,
                    FailureMessage = null,
                    NextRetryAt = null,
                    UpdatedAt = completedAt
                })
                .Where(x => x.Id == request.Id)
                .ExecuteCommandAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Account deletion processing failed. RequestId={RequestId}", request.RequestId);
            var retryCount = request.RetryCount + 1;
            var status = request.DataDeletedAt.HasValue ? NotificationPendingStatus : "failed";
            var updatedAt = DateTime.UtcNow;
            var nextRetryAt = updatedAt.AddMinutes(Math.Min(60, 1 << Math.Min(retryCount, 5)));
            await db.Updateable<AccountDeletionRequestEntity>()
                .SetColumns(x => new AccountDeletionRequestEntity
                {
                    Status = status,
                    DataDeletedAt = request.DataDeletedAt,
                    RetryCount = retryCount,
                    FailureMessage = ex.GetType().Name,
                    NextRetryAt = nextRetryAt,
                    UpdatedAt = updatedAt
                })
                .Where(x => x.Id == request.Id)
                .ExecuteCommandAsync(CancellationToken.None);
        }
    }

    private async Task DeleteUserDataAsync(AccountDeletionRequestEntity request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var anonymousName = $"deleted_{request.UserId}_{Hash(request.RequestId)[..12].ToLowerInvariant()}";
        var anonymousRegisterBusinessKey = $"register:deleted:{Hash(request.RequestId)[..12].ToLowerInvariant()}";
        var avatarUrl = await db.Queryable<SysUserEntity>()
            .Where(x => x.Id == request.UserId)
            .Select(x => x.AvatarUrl)
            .FirstAsync(cancellationToken);

        await refreshTokenStore.RevokeUserSessionsAsync(request.UserId, cancellationToken);
        DeleteDirectoryIfPresent(mediaPathResolver.ResolveFilePath(request.UserId.ToString()));
        DeleteLocalAvatarIfPresent(avatarUrl);

        await db.Ado.BeginTranAsync();
        try
        {
            await db.Updateable<AiImageFavoriteEntity>()
                .SetColumns(x => new AiImageFavoriteEntity { IsDeleted = true, UpdatedAt = now })
                .Where(x => x.UserId == request.UserId && !x.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);
            await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Prompt = "[deleted]",
                    NegativePrompt = null,
                    ReferenceImageUrls = null,
                    MaskImageUrl = null,
                    ResultUrls = null,
                    ErrorMessage = null,
                    IsDeleted = true,
                    UpdatedAt = now
                })
                .Where(x => x.UserId == request.UserId && !x.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);
            await db.Updateable<MediaAssetEntity>()
                .SetColumns(x => new MediaAssetEntity { IsDeleted = true, DeletedAt = now })
                .Where(x => x.OwnerUserId == request.UserId && !x.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<UserConsentEntity>()
                .Where(x => x.UserId == request.UserId)
                .ExecuteCommandAsync(cancellationToken);
            await db.Updateable<UserPointDetailEntity>()
                .SetColumns(x => new UserPointDetailEntity
                {
                    BusinessKey = anonymousRegisterBusinessKey,
                    Remark = null
                })
                .Where(x => x.UserId == request.UserId && x.Source == "register")
                .ExecuteCommandAsync(cancellationToken);
            await db.Updateable<UserPointDetailEntity>()
                .SetColumns(x => new UserPointDetailEntity { Remark = null })
                .Where(x => x.UserId == request.UserId && x.Remark != null)
                .ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<SysUserRoleEntity>().Where(x => x.UserId == request.UserId).ExecuteCommandAsync(cancellationToken);
            await db.Deleteable<SysUserSiteEntity>().Where(x => x.UserId == request.UserId).ExecuteCommandAsync(cancellationToken);
            await db.Updateable<SysLoginLogEntity>()
                .SetColumns(x => new SysLoginLogEntity { UserId = null, UserName = anonymousName, Ip = null, UserAgent = null })
                .Where(x => x.UserId == request.UserId)
                .ExecuteCommandAsync(cancellationToken);
            await db.Updateable<SysOperationLogEntity>()
                .SetColumns(x => new SysOperationLogEntity { UserId = null, RequestData = null, ResponseData = null, Ip = null })
                .Where(x => x.UserId == request.UserId)
                .ExecuteCommandAsync(cancellationToken);
            await db.Updateable<SysUserEntity>()
                .SetColumns(x => new SysUserEntity
                {
                    UserName = anonymousName,
                    NickName = "Deleted user",
                    PasswordHash = Hash(Guid.NewGuid().ToString("N")),
                    Salt = null,
                    Email = null,
                    Phone = null,
                    AvatarUrl = null,
                    Signature = null,
                    LastLoginIp = null,
                    Remark = null,
                    Status = 0,
                    IsSuperAdmin = false,
                    IsDeleted = true,
                    UpdatedAt = DateTime.Now
                })
                .Where(x => x.Id == request.UserId)
                .ExecuteCommandAsync(cancellationToken);
            await db.Updateable<AccountDeletionRequestEntity>()
                .SetColumns(x => new AccountDeletionRequestEntity
                {
                    DataDeletedAt = now,
                    Status = NotificationPendingStatus,
                    Reason = null,
                    NextRetryAt = null,
                    UpdatedAt = now
                })
                .Where(x => x.Id == request.Id)
                .ExecuteCommandAsync(cancellationToken);
            await db.Ado.CommitTranAsync();
            request.DataDeletedAt = now;
            request.Reason = null;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private void DeleteLocalAvatarIfPresent(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)
            || !Uri.TryCreate(avatarUrl, UriKind.Relative, out _)
            || !avatarUrl.StartsWith("/avatar/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var webRoot = Path.GetFullPath(environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        var avatarRoot = Path.GetFullPath(Path.Combine(webRoot, "avatar"));
        var relativePath = Uri.UnescapeDataString(avatarUrl["/avatar/".Length..])
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relativePath)) return;

        var candidate = Path.GetFullPath(Path.Combine(avatarRoot, relativePath));
        var avatarRootWithSeparator = avatarRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (candidate.StartsWith(avatarRootWithSeparator, comparison) && File.Exists(candidate))
        {
            File.Delete(candidate);
        }
    }

    private long RequireCurrentUser() =>
        currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "User is not authenticated.");

    private static void ValidateCreateRequest(CreateAccountDeletionRequest request)
    {
        if (!string.Equals(request.Confirmation?.Trim(), "DELETE", StringComparison.Ordinal))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "confirmation must equal DELETE.");
        }
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || request.CurrentPassword.Length > 256)
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "currentPassword is required.");
        }
        if (!Guid.TryParse(request.ClientRequestId, out _))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "clientRequestId must be a UUID.");
        }
        if (request.Reason?.Length > 500)
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "reason must not exceed 500 characters.");
        }
    }

    private static AccountDeletionRequestDto Map(AccountDeletionRequestEntity entity) => new()
    {
        RequestId = entity.RequestId,
        Status = entity.Status,
        RequestedAt = AsUtc(entity.RequestedAt),
        ScheduledDeletionAt = AsUtc(entity.ScheduledDeletionAt),
        CanCancel = entity.Status == ScheduledStatus && entity.ScheduledDeletionAt > DateTime.UtcNow,
        CompletedAt = entity.CompletedAt.HasValue ? AsUtc(entity.CompletedAt.Value) : null
    };

    private static string CreateRequestId(DateTime now) => $"ADR{now:yyyyMMdd}{Guid.NewGuid():N}"[..31].ToUpperInvariant();

    private static void EnsureMatchingFingerprint(AccountDeletionRequestEntity existing, string fingerprint)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(existing.RequestFingerprint),
                Convert.FromHexString(fingerprint)))
        {
            throw new AppException(
                ErrorCodes.Conflict,
                MachineErrorCodes.IdempotencyConflict,
                "The client request ID was already used with a different request.");
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
