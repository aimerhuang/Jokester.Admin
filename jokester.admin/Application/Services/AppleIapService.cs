using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using MySqlConnector;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AppleIapService(
    ISqlSugarClient db,
    ICurrentUser currentUser,
    IAppleAppStoreClient appStoreClient,
    IAppleJwsVerifier jwsVerifier,
    IAppleAppAccountTokenService accountTokenService,
    IOptions<AppleAppStoreOptions> options,
    ILogger<AppleIapService> logger) : IAppleIapService
{
    private readonly AppleAppStoreOptions _options = options.Value;

    public async Task<AppleTransactionFulfillmentDto> FulfillAsync(
        FulfillAppleTransactionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "User is not authenticated.");
        var transactionId = NormalizeTransactionId(request.TransactionId);
        var productId = NormalizeProductId(request.ProductId);
        if (!Guid.TryParse(idempotencyKey, out _))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Idempotency-Key must be a UUID.");
        }
        var idempotencyKeyHash = Hash(idempotencyKey.Trim());
        var submittedAppAccountToken = string.IsNullOrWhiteSpace(request.AppAccountToken)
            ? null
            : request.AppAccountToken.Trim().ToLowerInvariant();
        var requestFingerprint = Hash(JsonSerializer.Serialize(new
        {
            transactionId,
            productId,
            appAccountToken = submittedAppAccountToken
        }));

        var idempotent = await db.Queryable<AppleTransactionEntity>()
            .FirstAsync(x => x.UserId == userId && x.IdempotencyKeyHash == idempotencyKeyHash, cancellationToken);
        if (idempotent is not null)
        {
            if (!FixedTimeEquals(idempotent.RequestFingerprint, requestFingerprint))
            {
                throw new AppException(
                    ErrorCodes.Conflict,
                    MachineErrorCodes.IdempotencyConflict,
                    "The idempotency key was already used with a different Apple transaction request.");
            }
            return await MapFulfillmentAsync(idempotent, cancellationToken);
        }

        var existing = await db.Queryable<AppleTransactionEntity>()
            .FirstAsync(x => x.TransactionId == transactionId, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != userId) throw OwnedByOtherUser();
            return await MapFulfillmentAsync(existing, cancellationToken);
        }

        var product = await db.Queryable<AppleIapProductEntity>()
            .FirstAsync(x => x.AppleProductId == productId && x.Status == 1 && !x.IsDeleted, cancellationToken)
            ?? throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Apple product is not enabled.");
        var lookup = await appStoreClient.GetTransactionAsync(transactionId, product.Environment, cancellationToken);
        using var verified = jwsVerifier.Verify(lookup.SignedTransactionInfo);
        var snapshot = ParseTransaction(verified.Payload.RootElement, verified.Sha256);
        ValidateTransaction(snapshot, transactionId, productId, product, userId, submittedAppAccountToken);

        var now = DateTime.UtcNow;
        var entity = new AppleTransactionEntity
        {
            TransactionId = snapshot.TransactionId,
            IdempotencyKeyHash = idempotencyKeyHash,
            RequestFingerprint = requestFingerprint,
            OriginalTransactionId = snapshot.OriginalTransactionId,
            UserId = userId,
            ProductId = snapshot.ProductId,
            PackageId = product.PackageId,
            OrderNo = "IOS" + now.ToString("yyyyMMdd") + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
            Environment = snapshot.Environment,
            AppAccountToken = snapshot.AppAccountToken,
            Points = product.Points,
            Status = "fulfilled",
            SignedTransactionHash = snapshot.SignedTransactionHash,
            PurchaseDate = snapshot.PurchaseDate,
            FulfilledAt = now,
            CreatedAt = now
        };

        await db.Ado.BeginTranAsync();
        try
        {
            var user = await db.Queryable<SysUserEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.Id == userId && x.Status == 1 && !x.IsDeleted, cancellationToken)
                ?? throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.SessionRevoked, "The login session has been revoked.");
            var duplicateByKey = await db.Queryable<AppleTransactionEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.UserId == userId && x.IdempotencyKeyHash == idempotencyKeyHash, cancellationToken);
            if (duplicateByKey is not null)
            {
                if (!FixedTimeEquals(duplicateByKey.RequestFingerprint, requestFingerprint))
                    throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.IdempotencyConflict, "The idempotency key was already used with a different Apple transaction request.");
                await db.Ado.CommitTranAsync();
                return await MapFulfillmentAsync(duplicateByKey, cancellationToken);
            }
            var duplicate = await db.Queryable<AppleTransactionEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.TransactionId == transactionId, cancellationToken);
            if (duplicate is not null)
            {
                if (duplicate.UserId != userId) throw OwnedByOtherUser();
                await db.Ado.CommitTranAsync();
                return await MapFulfillmentAsync(duplicate, cancellationToken);
            }

            var balanceAfter = checked(user.PointBalance + product.Points);
            await db.Insertable(entity).ExecuteCommandAsync(cancellationToken);
            var updated = await db.Updateable<SysUserEntity>()
                .SetColumns(x => new SysUserEntity { PointBalance = balanceAfter, UpdatedAt = DateTime.Now })
                .Where(x => x.Id == userId && x.PointBalance == user.PointBalance && !x.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);
            if (updated != 1) throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.Conflict, "Point balance changed; retry the transaction.");
            await db.Insertable(new UserPointDetailEntity
            {
                UserId = userId,
                ChangePoints = product.Points,
                BalanceAfter = balanceAfter,
                ChangeType = "recharge",
                Source = "apple_iap",
                BusinessKey = $"apple:{transactionId}:fulfill",
                Remark = $"Apple IAP transaction {transactionId}",
                CreatedAt = DateTime.Now
            }).ExecuteCommandAsync(cancellationToken);
            await db.Ado.CommitTranAsync();
            return new AppleTransactionFulfillmentDto
            {
                TransactionId = transactionId,
                OrderNo = entity.OrderNo,
                Status = entity.Status,
                ProductId = entity.ProductId,
                AddedPoints = entity.Points,
                AvailablePoints = balanceAfter,
                FulfilledAt = now
            };
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            await db.Ado.RollbackTranAsync();
            logger.LogInformation(
                "Concurrent Apple fulfillment resolved through the unique transaction ledger. TransactionId={TransactionId}",
                transactionId);
            return await ResolveConcurrentFulfillmentAsync(
                userId,
                transactionId,
                idempotencyKeyHash,
                requestFingerprint,
                cancellationToken);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task<AppleTransactionFulfillmentDto> ResolveConcurrentFulfillmentAsync(
        long userId,
        string transactionId,
        string idempotencyKeyHash,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        var duplicateByKey = await db.Queryable<AppleTransactionEntity>()
            .FirstAsync(x => x.UserId == userId && x.IdempotencyKeyHash == idempotencyKeyHash, cancellationToken);
        if (duplicateByKey is not null)
        {
            if (!FixedTimeEquals(duplicateByKey.RequestFingerprint, requestFingerprint))
            {
                throw new AppException(
                    ErrorCodes.Conflict,
                    MachineErrorCodes.IdempotencyConflict,
                    "The idempotency key was already used with a different Apple transaction request.");
            }
            return await MapFulfillmentAsync(duplicateByKey, cancellationToken);
        }

        var duplicate = await db.Queryable<AppleTransactionEntity>()
            .FirstAsync(x => x.TransactionId == transactionId, cancellationToken);
        if (duplicate is not null)
        {
            if (duplicate.UserId != userId) throw OwnedByOtherUser();
            return await MapFulfillmentAsync(duplicate, cancellationToken);
        }

        throw new AppException(
            ErrorCodes.Conflict,
            MachineErrorCodes.Conflict,
            "The Apple transaction changed concurrently; retry the request.");
    }

    public async Task<AppleServerNotificationResponse> ReceiveNotificationAsync(
        AppleServerNotificationRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAppleNotificationsConfigured();
        using var verified = jwsVerifier.Verify(request.SignedPayload);
        var root = verified.Payload.RootElement;
        var notificationUuidValue = RequireString(root, "notificationUUID", 64);
        if (!Guid.TryParseExact(notificationUuidValue, "D", out var parsedNotificationUuid))
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                MachineErrorCodes.ValidationError,
                "Apple notification UUID is invalid.");
        }
        var notificationUuid = parsedNotificationUuid.ToString("D");
        var notificationType = RequireString(root, "notificationType", 80);
        var subtype = OptionalString(root, "subtype", 80);
        var data = root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object
            ? dataElement
            : default;
        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Apple notification data is missing.");
        }
        var environment = RequireString(data, "environment", 20);
        var notificationBundleId = RequireString(data, "bundleId", 200);
        if (!string.Equals(notificationBundleId, _options.BundleId, StringComparison.Ordinal))
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Apple notification bundle ID is invalid.");
        if (environment is not ("Production" or "Sandbox"))
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Apple notification environment is invalid.");

        AppleTransactionSnapshot? transaction = null;
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("signedTransactionInfo", out var signedTransaction)
            && !string.IsNullOrWhiteSpace(signedTransaction.GetString()))
        {
            using var transactionJws = jwsVerifier.Verify(signedTransaction.GetString()!);
            transaction = ParseTransaction(transactionJws.Payload.RootElement, transactionJws.Sha256);
            if (!string.Equals(transaction.BundleId, notificationBundleId, StringComparison.Ordinal)
                || !string.Equals(transaction.Environment, environment, StringComparison.Ordinal))
            {
                throw new AppException(
                    ErrorCodes.BadRequest,
                    MachineErrorCodes.ValidationError,
                    "Apple notification transaction context is invalid.");
            }
        }

        if (IsRefundNotification(notificationType) && transaction is null)
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                MachineErrorCodes.ValidationError,
                "Apple refund notification transaction data is missing.");
        }

        var existing = await db.Queryable<AppleServerNotificationEntity>()
            .FirstAsync(x => x.NotificationUuid == notificationUuid, cancellationToken);
        if (existing is not null)
        {
            EnsureMatchingNotificationPayload(existing, verified.Sha256);
            return new AppleServerNotificationResponse { NotificationUuid = notificationUuid, Status = "accepted" };
        }

        var entity = new AppleServerNotificationEntity
        {
            NotificationUuid = notificationUuid,
            NotificationType = notificationType,
            Subtype = subtype,
            Environment = environment,
            TransactionId = transaction?.TransactionId,
            SignedPayloadHash = verified.Sha256,
            Status = "received",
            ReceivedAt = DateTime.UtcNow
        };
        var acceptedDuplicate = false;
        await db.Ado.BeginTranAsync();
        try
        {
            var duplicate = await db.Queryable<AppleServerNotificationEntity>()
                .TranLock(DbLockType.Wait)
                .FirstAsync(x => x.NotificationUuid == notificationUuid, cancellationToken);
            if (duplicate is null)
            {
                await db.Insertable(entity).ExecuteCommandAsync(cancellationToken);
            }
            else
            {
                EnsureMatchingNotificationPayload(duplicate, verified.Sha256);
                acceptedDuplicate = true;
            }
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        if (acceptedDuplicate)
        {
            return new AppleServerNotificationResponse { NotificationUuid = notificationUuid, Status = "accepted" };
        }

        if (!IsRefundNotification(notificationType))
        {
            await MarkNotificationProcessedAsync(notificationUuid, CancellationToken.None);
        }
        else
        {
            await TryProcessNotificationAsync(entity, transaction!, CancellationToken.None);
        }
        return new AppleServerNotificationResponse { NotificationUuid = notificationUuid, Status = "accepted" };
    }

    public async Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken)
    {
        var pending = await db.Queryable<AppleServerNotificationEntity>()
            .Where(x => (x.Status == "received" || x.Status == "failed") && x.RetryCount < 20)
            .OrderBy(x => x.ReceivedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            if (!IsRefundNotification(notification.NotificationType))
            {
                await MarkNotificationProcessedAsync(notification.NotificationUuid, cancellationToken);
                continue;
            }

            if (string.IsNullOrWhiteSpace(notification.TransactionId))
            {
                var exception = new InvalidOperationException(
                    "The Apple refund notification is missing its transaction ID.");
                logger.LogError(
                    exception,
                    "Apple notification retry failed. NotificationUuid={NotificationUuid}, TransactionId={TransactionId}",
                    notification.NotificationUuid,
                    notification.TransactionId);
                await MarkNotificationFailedAsync(notification, exception, CancellationToken.None);
                continue;
            }

            try
            {
                var lookupEnvironment = string.Equals(notification.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase)
                    ? "Sandbox"
                    : "Production";
                var lookup = await appStoreClient.GetTransactionAsync(
                    notification.TransactionId,
                    lookupEnvironment,
                    cancellationToken);
                using var verified = jwsVerifier.Verify(lookup.SignedTransactionInfo);
                var transaction = ParseTransaction(verified.Payload.RootElement, verified.Sha256);
                ValidateNotificationTransactionContext(notification, transaction);
                await TryProcessNotificationAsync(notification, transaction, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Apple notification retry failed. NotificationUuid={NotificationUuid}, TransactionId={TransactionId}",
                    notification.NotificationUuid,
                    notification.TransactionId);
                await MarkNotificationFailedAsync(notification, ex, CancellationToken.None);
            }
        }
    }

    private async Task TryProcessNotificationAsync(
        AppleServerNotificationEntity notification,
        AppleTransactionSnapshot transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.Ado.BeginTranAsync();
            await ApplyRefundAsync(transaction, cancellationToken);
            await MarkNotificationProcessedAsync(notification.NotificationUuid, cancellationToken);
            await db.Ado.CommitTranAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await db.Ado.RollbackTranAsync();
            logger.LogError(
                ex,
                "Apple notification processing failed. NotificationUuid={NotificationUuid}, TransactionId={TransactionId}",
                notification.NotificationUuid,
                transaction.TransactionId);
            await MarkNotificationFailedAsync(notification, ex, CancellationToken.None);
        }
    }

    private async Task ApplyRefundAsync(AppleTransactionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var transaction = await db.Queryable<AppleTransactionEntity>()
            .FirstAsync(x => x.TransactionId == snapshot.TransactionId, cancellationToken);
        if (transaction is null)
            throw new InvalidOperationException("The refunded Apple transaction has not been fulfilled locally.");

        var user = await db.Queryable<SysUserEntity>()
            .TranLock(DbLockType.Wait)
            .FirstAsync(x => x.Id == transaction.UserId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException("The Apple transaction owner no longer exists.");

        transaction = await db.Queryable<AppleTransactionEntity>()
            .TranLock(DbLockType.Wait)
            .FirstAsync(x => x.TransactionId == snapshot.TransactionId, cancellationToken);
        if (transaction is null || transaction.UserId != user.Id)
            throw new InvalidOperationException("The refunded Apple transaction changed during processing.");
        if (transaction.Status == "refunded") return;
        if (transaction.Status != "fulfilled")
            throw new InvalidOperationException("The Apple transaction is not in a refundable state.");
        if (!string.Equals(snapshot.BundleId, _options.BundleId, StringComparison.Ordinal)
            || !string.Equals(snapshot.ProductId, transaction.ProductId, StringComparison.Ordinal)
            || !string.Equals(snapshot.Environment, transaction.Environment, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(snapshot.AppAccountToken, transaction.AppAccountToken, StringComparison.OrdinalIgnoreCase)
            || snapshot.Quantity != 1
            || !string.Equals(snapshot.ProductType, "Consumable", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The refund transaction does not match the fulfilled Apple transaction.");
        }
        var deducted = Math.Min(user.PointBalance, transaction.Points);
        var balanceAfter = user.PointBalance - deducted;
        var debt = transaction.Points - deducted;
        var userUpdated = await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity { PointBalance = balanceAfter, UpdatedAt = DateTime.Now })
            .Where(x => x.Id == user.Id && x.PointBalance == user.PointBalance)
            .ExecuteCommandAsync(cancellationToken);
        if (userUpdated != 1)
            throw new InvalidOperationException("The point balance changed during Apple refund processing.");
        await db.Insertable(new UserPointDetailEntity
        {
            UserId = user.Id,
            ChangePoints = -deducted,
            BalanceAfter = balanceAfter,
            ChangeType = "refund",
            Source = "apple_refund",
            BusinessKey = $"apple:{transaction.TransactionId}:refund",
            Remark = debt > 0 ? $"Apple refund; {debt} points recorded as debt" : "Apple refund",
            CreatedAt = DateTime.Now
        }).ExecuteCommandAsync(cancellationToken);
        if (debt > 0)
        {
            await db.Insertable(new AppleIapDebtEntity
            {
                UserId = user.Id,
                TransactionId = transaction.TransactionId,
                PointsOwed = debt,
                Status = "open",
                CreatedAt = DateTime.UtcNow
            }).ExecuteCommandAsync(cancellationToken);
        }
        var transactionUpdated = await db.Updateable<AppleTransactionEntity>()
            .SetColumns(x => new AppleTransactionEntity
            {
                Status = "refunded",
                RevocationDate = snapshot.RevocationDate ?? DateTime.UtcNow,
                RefundedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => x.Id == transaction.Id && x.Status == "fulfilled")
            .ExecuteCommandAsync(cancellationToken);
        if (transactionUpdated != 1)
            throw new InvalidOperationException("The Apple transaction state changed during refund processing.");
    }

    private async Task MarkNotificationFailedAsync(
        AppleServerNotificationEntity entity,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await db.Updateable<AppleServerNotificationEntity>()
            .SetColumns(x => new AppleServerNotificationEntity
            {
                Status = "failed",
                RetryCount = entity.RetryCount + 1,
                FailureMessage = exception.GetType().Name
            })
            .Where(x => x.NotificationUuid == entity.NotificationUuid && x.Status != "processed")
            .ExecuteCommandAsync(cancellationToken);
    }

    private async Task MarkNotificationProcessedAsync(string notificationUuid, CancellationToken cancellationToken)
    {
        await db.Updateable<AppleServerNotificationEntity>()
            .SetColumns(x => new AppleServerNotificationEntity
            {
                Status = "processed",
                ProcessedAt = DateTime.UtcNow,
                FailureMessage = null
            })
            .Where(x => x.NotificationUuid == notificationUuid && x.Status != "processed")
            .ExecuteCommandAsync(cancellationToken);
    }

    private void ValidateTransaction(
        AppleTransactionSnapshot snapshot,
        string transactionId,
        string productId,
        AppleIapProductEntity product,
        long userId,
        string? submittedAppAccountToken)
    {
        var expectedAccountToken = accountTokenService.GetForUser(userId);
        if (!string.Equals(snapshot.TransactionId, transactionId, StringComparison.Ordinal)
            || !string.Equals(snapshot.ProductId, productId, StringComparison.Ordinal)
            || !string.Equals(snapshot.BundleId, _options.BundleId, StringComparison.Ordinal)
            || snapshot.RevocationDate.HasValue
            || snapshot.Quantity != 1
            || !string.Equals(snapshot.ProductType, "Consumable", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(product.ProductType, "consumable", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(snapshot.AppAccountToken, expectedAccountToken, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(submittedAppAccountToken)
                && !string.Equals(submittedAppAccountToken, expectedAccountToken, StringComparison.OrdinalIgnoreCase))
            || (product.Environment != "Both"
                && !string.Equals(snapshot.Environment, product.Environment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Apple transaction validation failed.");
        }
    }

    private static AppleTransactionSnapshot ParseTransaction(JsonElement root, string hash)
    {
        return new AppleTransactionSnapshot(
            RequireString(root, "transactionId", 64),
            RequireString(root, "originalTransactionId", 64),
            RequireString(root, "productId", 200),
            RequireString(root, "bundleId", 200),
            RequireString(root, "environment", 20),
            RequireString(root, "appAccountToken", 64),
            RequireString(root, "type", 50),
            ReadUnixMilliseconds(root, "purchaseDate", required: true)!.Value,
            ReadUnixMilliseconds(root, "revocationDate", required: false),
            ReadRequiredInt32(root, "quantity"),
            hash);
    }

    private async Task<AppleTransactionFulfillmentDto> MapFulfillmentAsync(
        AppleTransactionEntity entity,
        CancellationToken cancellationToken)
    {
        var balance = await db.Queryable<SysUserEntity>()
            .Where(x => x.Id == entity.UserId)
            .Select(x => x.PointBalance)
            .FirstAsync(cancellationToken);
        return new AppleTransactionFulfillmentDto
        {
            TransactionId = entity.TransactionId,
            OrderNo = entity.OrderNo,
            Status = entity.Status,
            ProductId = entity.ProductId,
            AddedPoints = entity.Points,
            AvailablePoints = balance,
            FulfilledAt = ApiDateTime.FromUtcStorage(entity.FulfilledAt ?? entity.CreatedAt)
        };
    }

    private static bool IsRefundNotification(string type) => type is "REFUND" or "REVOKE";

    private void ValidateNotificationTransactionContext(
        AppleServerNotificationEntity notification,
        AppleTransactionSnapshot transaction)
    {
        if (!string.Equals(transaction.TransactionId, notification.TransactionId, StringComparison.Ordinal)
            || !string.Equals(transaction.BundleId, _options.BundleId, StringComparison.Ordinal)
            || !string.Equals(transaction.Environment, notification.Environment, StringComparison.Ordinal)
            || transaction.Quantity != 1
            || !string.Equals(transaction.ProductType, "Consumable", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                MachineErrorCodes.ValidationError,
                "Apple notification transaction context is invalid.");
        }
    }

    private void EnsureAppleNotificationsConfigured()
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BundleId))
        {
            throw new AppException(
                ErrorCodes.ServiceUnavailable,
                MachineErrorCodes.ServiceUnavailable,
                "Apple server notifications are not configured.");
        }
    }

    private static string RequireString(JsonElement root, string name, int maxLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, $"Apple payload field {name} is missing.");
        var result = value.GetString()?.Trim() ?? string.Empty;
        if (result.Length == 0 || result.Length > maxLength)
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, $"Apple payload field {name} is invalid.");
        return result;
    }

    private static string? OptionalString(JsonElement root, string name, int maxLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        var result = value.GetString()?.Trim();
        return string.IsNullOrEmpty(result) || result.Length > maxLength ? null : result;
    }

    private static DateTime? ReadUnixMilliseconds(JsonElement root, string name, bool required)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt64(out var milliseconds))
        {
            if (required) throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, $"Apple payload field {name} is missing.");
            return null;
        }
        try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, $"Apple payload field {name} is invalid."); }
    }

    private static int ReadRequiredInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                MachineErrorCodes.ValidationError,
                $"Apple payload field {name} is missing.");
        }
        return result;
    }

    private static void EnsureMatchingNotificationPayload(
        AppleServerNotificationEntity existing,
        string signedPayloadHash)
    {
        if (!FixedTimeEquals(existing.SignedPayloadHash, signedPayloadHash))
        {
            throw new AppException(
                ErrorCodes.Conflict,
                MachineErrorCodes.IdempotencyConflict,
                "The Apple notification UUID was already used with a different payload.");
        }
    }

    private static string NormalizeTransactionId(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 6 or > 64 || normalized.Any(ch => !char.IsDigit(ch)))
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "transactionId is invalid.");
        return normalized;
    }

    private static string NormalizeProductId(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 3 or > 200 || normalized.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')))
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "productId is invalid.");
        return normalized;
    }

    private static AppException OwnedByOtherUser() => new(
        ErrorCodes.Conflict,
        MachineErrorCodes.AppleTransactionOwnedByOtherUser,
        "The Apple transaction belongs to another user.");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
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

    private sealed record AppleTransactionSnapshot(
        string TransactionId,
        string OriginalTransactionId,
        string ProductId,
        string BundleId,
        string Environment,
        string AppAccountToken,
        string ProductType,
        DateTime PurchaseDate,
        DateTime? RevocationDate,
        int Quantity,
        string SignedTransactionHash);
}
