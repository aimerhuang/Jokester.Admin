using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AppleIapServiceTests
{
    [Fact]
    public async Task FulfillAsync_RepeatedRequestCreditsTransactionExactlyOnce()
    {
        using var context = new TestContext(initialBalance: 25);
        var request = new FulfillAppleTransactionRequest
        {
            TransactionId = TestContext.TransactionId,
            ProductId = TestContext.ProductId,
            AppAccountToken = TestContext.AccountToken
        };
        var idempotencyKey = Guid.NewGuid().ToString("D");

        var first = await context.Service.FulfillAsync(request, idempotencyKey, default);
        var repeated = first;
        for (var replay = 1; replay < 100; replay++)
        {
            repeated = await context.Service.FulfillAsync(request, idempotencyKey, default);
        }

        Assert.Equal(first.OrderNo, repeated.OrderNo);
        Assert.Equal(DateTimeKind.Utc, first.FulfilledAt.Kind);
        Assert.Equal(DateTimeKind.Utc, repeated.FulfilledAt.Kind);
        Assert.Equal(100, first.AddedPoints);
        Assert.Equal(125, repeated.AvailablePoints);
        Assert.Equal(125, context.User.PointBalance);
        Assert.Single(context.Db.Queryable<AppleTransactionEntity>().ToList());
        var bucket = Assert.Single(context.Db.Queryable<PointBucketEntity>().ToList());
        Assert.Null(bucket.ExpiresAt);
        Assert.Equal(200, bucket.SpendPriority);
        var detail = Assert.Single(context.Db.Queryable<UserPointDetailEntity>().ToList());
        Assert.Equal(100, detail.ChangePoints);
        Assert.Equal("apple_iap", detail.Source);
        Assert.Empty(context.Db.Queryable<SysUserMembershipEntitlementEntity>().ToList());
        context.AppStoreClient.Verify(
            client => client.GetTransactionAsync(TestContext.TransactionId, "Production", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FulfillAsync_ExpiringPackageCreatesBucketFromFulfillmentTime()
    {
        using var context = new TestContext(
            initialBalance: 25,
            validityDays: 30,
            packageCode: "monthly",
            packagePoints: 5_000);
        var before = DateTime.Now.AddDays(30);

        var result = await context.Service.FulfillAsync(
            CreateFulfillmentRequest(TestContext.AccountToken),
            Guid.NewGuid().ToString("D"),
            default);

        var after = DateTime.Now.AddDays(30);
        var bucket = Assert.Single(context.Db.Queryable<PointBucketEntity>().ToList());
        Assert.Equal(5_000, bucket.GrantedPoints);
        Assert.Equal(5_000, bucket.RemainingPoints);
        Assert.InRange(bucket.ExpiresAt!.Value, before, after);
        Assert.Equal(0, bucket.SpendPriority);
        Assert.NotNull(result.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, result.ExpiresAt!.Value.Kind);
        var entitlement = Assert.Single(
            context.Db.Queryable<SysUserMembershipEntitlementEntity>().ToList());
        Assert.Equal("monthly_vip", entitlement.TierCode);
        Assert.Equal("apple_iap", entitlement.Source);
        Assert.Equal($"apple:{TestContext.TransactionId}:fulfill", entitlement.BusinessKey);
        Assert.Equal("active", entitlement.Status);
        Assert.Null(entitlement.RevokedAt);
        Assert.Equal(bucket.ExpiresAt.Value, entitlement.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FulfillAsync_RejectsMonthlyApplePointMismatch()
    {
        using var context = new TestContext(
            initialBalance: 0,
            validityDays: 30,
            packageCode: "monthly",
            packagePoints: 5_000,
            applePoints: 4_999);

        var exception = await Assert.ThrowsAsync<AppException>(() => context.FulfillAsync());

        Assert.Equal(ErrorCodes.ServerError, exception.Code);
        Assert.Equal(0, context.User.PointBalance);
        Assert.Empty(context.Db.Queryable<PointBucketEntity>().ToList());
        context.AppStoreClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FulfillAsync_RejectsDisabledMappedPackage()
    {
        using var context = new TestContext(initialBalance: 0, packageStatus: 0);

        var exception = await Assert.ThrowsAsync<AppException>(() => context.FulfillAsync());

        Assert.Equal(ErrorCodes.ServerError, exception.Code);
        Assert.Equal(0, context.User.PointBalance);
        context.AppStoreClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FulfillAsync_SameIdempotencyKeyWithDifferentAppAccountTokenConflicts()
    {
        using var context = new TestContext(initialBalance: 25);
        var idempotencyKey = Guid.NewGuid().ToString("D");
        await context.Service.FulfillAsync(
            CreateFulfillmentRequest(TestContext.AccountToken),
            idempotencyKey,
            default);

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.FulfillAsync(
            CreateFulfillmentRequest(Guid.NewGuid().ToString("D")),
            idempotencyKey,
            default));

        Assert.Equal(ErrorCodes.Conflict, exception.Code);
        Assert.Equal(MachineErrorCodes.IdempotencyConflict, exception.MachineCode);
        Assert.Equal(125, context.User.PointBalance);
        Assert.Single(context.Db.Queryable<AppleTransactionEntity>().ToList());
    }

    private static FulfillAppleTransactionRequest CreateFulfillmentRequest(string? accountBinding)
    {
        var request = new FulfillAppleTransactionRequest
        {
            TransactionId = TestContext.TransactionId,
            ProductId = TestContext.ProductId
        };
        var propertyName = string.Concat("App", "Account", "Token");
        typeof(FulfillAppleTransactionRequest).GetProperty(propertyName)!.SetValue(request, accountBinding);
        return request;
    }

    [Fact]
    public async Task ReceiveNotificationAsync_RefundShortfallCreatesDebtAndIsIdempotent()
    {
        using var context = new TestContext(initialBalance: 0);
        await context.FulfillAsync();
        context.Db.Updateable<SysUserEntity>()
            .SetColumns(user => user.PointBalance == 30)
            .Where(user => user.Id == 1)
            .ExecuteCommand();
        context.Db.Updateable<PointBucketEntity>()
            .SetColumns(bucket => bucket.RemainingPoints == 30)
            .Where(bucket => bucket.UserId == 1)
            .ExecuteCommand();
        var notificationUuid = Guid.NewGuid().ToString("D");
        context.ConfigureRefundNotification(notificationUuid, outerHash: new string('B', 64));

        var response = await context.Service.ReceiveNotificationAsync(
            new AppleServerNotificationRequest { SignedPayload = "refund-notification" },
            default);
        var repeated = await context.Service.ReceiveNotificationAsync(
            new AppleServerNotificationRequest { SignedPayload = "refund-notification" },
            default);

        Assert.Equal(notificationUuid, response.NotificationUuid);
        Assert.Equal(response.NotificationUuid, repeated.NotificationUuid);
        Assert.Equal(0, context.User.PointBalance);
        var transaction = context.Db.Queryable<AppleTransactionEntity>().Single();
        Assert.Equal("refunded", transaction.Status);
        var debt = context.Db.Queryable<AppleIapDebtEntity>().Single();
        Assert.Equal(70, debt.PointsOwed);
        Assert.Equal("open", debt.Status);
        var refund = context.Db.Queryable<UserPointDetailEntity>()
            .Single(detail => detail.Source == "apple_refund");
        Assert.Equal(-30, refund.ChangePoints);
        Assert.Equal("processed", context.Db.Queryable<AppleServerNotificationEntity>().Single().Status);
    }

    [Fact]
    public async Task ReceiveNotificationAsync_RefundConsumesExpiringBucketAllocation()
    {
        using var context = new TestContext(
            initialBalance: 0,
            validityDays: 30,
            packageCode: "monthly",
            packagePoints: 5000);
        await context.FulfillAsync();
        var notificationUuid = Guid.NewGuid().ToString("D");
        context.ConfigureRefundNotification(notificationUuid, outerHash: new string('B', 64));

        await context.Service.ReceiveNotificationAsync(
            new AppleServerNotificationRequest { SignedPayload = "refund-notification" },
            default);

        Assert.Equal(0, context.User.PointBalance);
        Assert.Equal(0, Assert.Single(context.Db.Queryable<PointBucketEntity>().ToList()).RemainingPoints);
        var usage = Assert.Single(context.Db.Queryable<PointBucketUsageEntity>().ToList());
        Assert.Equal(5000, usage.UsedPoints);
        Assert.Equal($"apple:{TestContext.TransactionId}:refund", usage.BusinessKey);
        Assert.Empty(context.Db.Queryable<AppleIapDebtEntity>().ToList());
        var entitlement = Assert.Single(
            context.Db.Queryable<SysUserMembershipEntitlementEntity>().ToList());
        Assert.Equal("revoked", entitlement.Status);
        Assert.NotNull(entitlement.RevokedAt);
    }

    [Fact]
    public async Task ReceiveNotificationAsync_PermanentGrantRefundDoesNotConsumeUnrelatedExpiringBucket()
    {
        using var context = new TestContext(initialBalance: 100);
        context.Db.Insertable(new PointBucketEntity
        {
            UserId = 1,
            Source = "recharge",
            BusinessKey = "redeem:unrelated-monthly",
            GrantedPoints = 100,
            RemainingPoints = 100,
            ExpiresAt = DateTime.Now.AddDays(30),
            SpendPriority = 0,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();
        await context.FulfillAsync();
        context.ConfigureRefundNotification(Guid.NewGuid().ToString("D"), outerHash: new string('B', 64));

        await context.Service.ReceiveNotificationAsync(
            new AppleServerNotificationRequest { SignedPayload = "refund-notification" },
            default);

        Assert.Equal(100, context.User.PointBalance);
        var buckets = context.Db.Queryable<PointBucketEntity>().ToList();
        var unrelatedBucket = Assert.Single(buckets, bucket => bucket.BusinessKey == "redeem:unrelated-monthly");
        Assert.Equal(100, unrelatedBucket.RemainingPoints);
        var appleBucket = Assert.Single(
            buckets,
            bucket => bucket.BusinessKey == $"apple:{TestContext.TransactionId}:fulfill");
        Assert.Equal(0, appleBucket.RemainingPoints);
        var usage = Assert.Single(context.Db.Queryable<PointBucketUsageEntity>().ToList());
        Assert.Equal(appleBucket.Id, usage.BucketId);
        Assert.Equal(100, usage.UsedPoints);
        Assert.Empty(context.Db.Queryable<AppleIapDebtEntity>().ToList());
    }

    [Fact]
    public async Task ReceiveNotificationAsync_LegacyGrantRefundDoesNotConsumeLaterTrackedBucket()
    {
        using var context = new TestContext(initialBalance: 0);
        await context.FulfillAsync();
        context.Db.Deleteable<PointBucketEntity>().ExecuteCommand();
        context.Db.Updateable<SysUserEntity>()
            .SetColumns(user => user.PointBalance == 50)
            .Where(user => user.Id == 1)
            .ExecuteCommand();
        var laterMonthly = new PointBucketEntity
        {
            UserId = 1,
            Source = "recharge",
            BusinessKey = "recharge:later-monthly",
            GrantedPoints = 100,
            RemainingPoints = 100,
            ExpiresAt = DateTime.Now.AddDays(30),
            SpendPriority = 0,
            CreatedAt = DateTime.Now
        };
        laterMonthly.Id = context.Db.Insertable(laterMonthly).ExecuteReturnBigIdentity();
        context.Db.Updateable<SysUserEntity>()
            .SetColumns(user => user.PointBalance == 150)
            .Where(user => user.Id == 1)
            .ExecuteCommand();
        context.ConfigureRefundNotification(Guid.NewGuid().ToString("D"), outerHash: new string('B', 64));

        await context.Service.ReceiveNotificationAsync(
            new AppleServerNotificationRequest { SignedPayload = "refund-notification" },
            default);

        Assert.Equal(100, context.User.PointBalance);
        Assert.Equal(100, context.Db.Queryable<PointBucketEntity>().Single(x => x.Id == laterMonthly.Id).RemainingPoints);
        Assert.Empty(context.Db.Queryable<PointBucketUsageEntity>().ToList());
        var debt = Assert.Single(context.Db.Queryable<AppleIapDebtEntity>().ToList());
        Assert.Equal(50, debt.PointsOwed);
    }

    [Fact]
    public async Task ReceiveNotificationAsync_ExpiredGrantRefundDoesNotClawBackOrCreateDebt()
    {
        using var context = new TestContext(
            initialBalance: 0,
            validityDays: 30,
            packageCode: "monthly",
            packagePoints: 5000);
        await context.FulfillAsync();
        var bucket = Assert.Single(context.Db.Queryable<PointBucketEntity>().ToList());
        context.Db.Updateable<PointBucketEntity>()
            .SetColumns(value => value.ExpiresAt == DateTime.Now.AddMinutes(-1))
            .Where(value => value.Id == bucket.Id)
            .ExecuteCommand();
        context.ConfigureRefundNotification(Guid.NewGuid().ToString("D"), outerHash: new string('B', 64));

        await context.Service.ReceiveNotificationAsync(
            new AppleServerNotificationRequest { SignedPayload = "refund-notification" },
            default);

        Assert.Equal(0, context.User.PointBalance);
        bucket = Assert.Single(context.Db.Queryable<PointBucketEntity>().ToList());
        Assert.Equal(0, bucket.RemainingPoints);
        Assert.Equal(5000, bucket.ExpiredPoints);
        Assert.Empty(context.Db.Queryable<PointBucketUsageEntity>().ToList());
        Assert.Empty(context.Db.Queryable<AppleIapDebtEntity>().ToList());
    }

    [Theory]
    [InlineData(null, "basic", 100)]
    [InlineData(30, "monthly", 5000)]
    public async Task ReceiveNotificationAsync_ReservedGrantPointsAreNotClawedBackOrRecordedAsDebt(
        int? validityDays,
        string packageCode,
        int packagePoints)
    {
        using var context = new TestContext(
            initialBalance: 25,
            validityDays,
            packageCode,
            packagePoints);
        await context.FulfillAsync();
        var bucket = Assert.Single(context.Db.Queryable<PointBucketEntity>().ToList());
        const int reservedPoints = 60;
        context.Db.Updateable<PointBucketEntity>()
            .SetColumns(value => new PointBucketEntity
            {
                RemainingPoints = value.RemainingPoints - reservedPoints,
                UpdatedAt = DateTime.Now
            })
            .Where(value => value.Id == bucket.Id)
            .ExecuteCommand();
        context.Db.Updateable<SysUserEntity>()
            .SetColumns(value => value.PointBalance == value.PointBalance - reservedPoints)
            .Where(value => value.Id == 1)
            .ExecuteCommand();
        var task = new AiImageTaskEntity
        {
            SiteId = 1,
            UserId = 1,
            Prompt = "pending Apple refund test",
            ImageCount = 1,
            IdempotencyKey = "pending-apple-refund",
            RequestFingerprint = "pending-apple-refund-fingerprint",
            PointCost = reservedPoints,
            BillingStatus = 0,
            ResolutionCode = "1k",
            QualityCode = "med",
            AspectRatioCode = "1:1",
            Width = 1024,
            Height = 1024,
            Size = "1024x1024",
            Quality = "medium",
            Status = 3,
            CreatedAt = DateTime.Now
        };
        task.Id = context.Db.Insertable(task).ExecuteReturnBigIdentity();
        context.Db.Insertable(new PointBucketUsageEntity
        {
            BucketId = bucket.Id,
            UserId = 1,
            BusinessKey = $"image:{task.Id}:reserve",
            UsedPoints = reservedPoints,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();
        context.ConfigureRefundNotification(Guid.NewGuid().ToString("D"), outerHash: new string('B', 64));

        await context.Service.ReceiveNotificationAsync(
            new AppleServerNotificationRequest { SignedPayload = "refund-notification" },
            default);

        Assert.Equal(25, context.User.PointBalance);
        Assert.Equal(0, Assert.Single(context.Db.Queryable<PointBucketEntity>().ToList()).RemainingPoints);
        var usages = context.Db.Queryable<PointBucketUsageEntity>().OrderBy(value => value.Id).ToList();
        Assert.Collection(
            usages,
            reservation =>
            {
                Assert.Equal($"image:{task.Id}:reserve", reservation.BusinessKey);
                Assert.Equal(60, reservation.UsedPoints);
                Assert.Equal(60, reservation.DeferredClawbackPoints);
                Assert.Equal($"apple:{TestContext.TransactionId}:refund", reservation.DeferredClawbackBusinessKey);
            },
            clawback =>
            {
                Assert.Equal($"apple:{TestContext.TransactionId}:refund", clawback.BusinessKey);
                Assert.Equal(packagePoints - reservedPoints, clawback.UsedPoints);
            });
        Assert.Empty(context.Db.Queryable<AppleIapDebtEntity>().ToList());
    }

    [Fact]
    public async Task ReceiveNotificationAsync_RejectsSameUuidWithDifferentPayload()
    {
        using var context = new TestContext(initialBalance: 0);
        var notificationUuid = Guid.NewGuid().ToString("D");
        context.ConfigureTestNotification(notificationUuid, "test-notification-a", new string('C', 64));
        await context.Service.ReceiveNotificationAsync(
            new AppleServerNotificationRequest { SignedPayload = "test-notification-a" },
            default);
        context.ConfigureTestNotification(notificationUuid, "test-notification-b", new string('D', 64));

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.ReceiveNotificationAsync(
            new AppleServerNotificationRequest { SignedPayload = "test-notification-b" },
            default));

        Assert.Equal(ErrorCodes.Conflict, exception.Code);
        Assert.Equal(MachineErrorCodes.IdempotencyConflict, exception.MachineCode);
    }

    [Fact]
    public async Task ReceiveNotificationAsync_RequiresTransactionForRefund()
    {
        using var context = new TestContext(initialBalance: 0);
        context.ConfigureOuterNotification(
            "refund-without-transaction",
            Guid.NewGuid().ToString("D"),
            "REFUND",
            new string('E', 64),
            includeTransaction: false);

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.ReceiveNotificationAsync(
            new AppleServerNotificationRequest { SignedPayload = "refund-without-transaction" },
            default));

        Assert.Equal(ErrorCodes.BadRequest, exception.Code);
        Assert.Empty(context.Db.Queryable<AppleServerNotificationEntity>().ToList());
    }

    [Fact]
    public async Task ProcessPendingNotificationsAsync_RefundWithoutTransactionRemainsRetryable()
    {
        using var context = new TestContext(initialBalance: 0);
        context.Db.Insertable(new AppleServerNotificationEntity
        {
            NotificationUuid = Guid.NewGuid().ToString("D"),
            NotificationType = "REFUND",
            Environment = "Production",
            TransactionId = null,
            SignedPayloadHash = new string('A', 64),
            Status = "received",
            ReceivedAt = DateTime.UtcNow
        }).ExecuteCommand();

        await context.Service.ProcessPendingNotificationsAsync(default);

        var notification = context.Db.Queryable<AppleServerNotificationEntity>().Single();
        Assert.Equal("failed", notification.Status);
        Assert.Equal(1, notification.RetryCount);
        Assert.Null(notification.ProcessedAt);
        Assert.Equal(nameof(InvalidOperationException), notification.FailureMessage);
        Assert.Empty(context.Db.Queryable<UserPointDetailEntity>().ToList());
        context.AppStoreClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessPendingNotificationsAsync_RetriesRefundBeyondLegacyAttemptLimit()
    {
        using var context = new TestContext(
            initialBalance: 0,
            validityDays: 30,
            packageCode: "monthly",
            packagePoints: 5000);
        await context.FulfillAsync();
        var notificationUuid = Guid.NewGuid().ToString("D");
        context.ConfigureRefundNotification(notificationUuid, outerHash: new string('B', 64));
        context.AppStoreClient.Setup(client => client.GetTransactionAsync(
                TestContext.TransactionId,
                "Production",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppleTransactionLookupResult("refund-transaction", "Production"));
        context.Db.Insertable(new AppleServerNotificationEntity
        {
            NotificationUuid = notificationUuid,
            NotificationType = "REFUND",
            Environment = "Production",
            TransactionId = TestContext.TransactionId,
            SignedPayloadHash = new string('B', 64),
            Status = "failed",
            RetryCount = 20,
            FailureMessage = nameof(InvalidOperationException),
            ReceivedAt = DateTime.UtcNow.AddMinutes(-20)
        }).ExecuteCommand();

        await context.Service.ProcessPendingNotificationsAsync(default);

        Assert.Equal(0, context.User.PointBalance);
        var notification = context.Db.Queryable<AppleServerNotificationEntity>().Single();
        Assert.Equal("processed", notification.Status);
        Assert.Equal(20, notification.RetryCount);
        Assert.Null(notification.FailureMessage);
        Assert.Equal(
            "refunded",
            context.Db.Queryable<AppleTransactionEntity>().Single().Status);
        Assert.Equal(
            5000,
            Assert.Single(context.Db.Queryable<PointBucketUsageEntity>().ToList()).UsedPoints);
    }

    private sealed class TestContext : IDisposable
    {
        public const string TransactionId = "2000000123456789";
        public const string ProductId = "cc.jokester.ai.credits.100";
        public const string AccountToken = "778899aa-bbcc-8dde-8f00-112233445566";
        private const string BundleId = "cc.jokester.ai";
        private readonly Mock<IAppleJwsVerifier> _jwsVerifier = new();

        public TestContext(
            int initialBalance,
            int? validityDays = null,
            string packageCode = "basic",
            int packagePoints = 100,
            int? applePoints = null,
            int packageStatus = 1)
        {
            SQLitePCL.Batteries_V2.Init();
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=:memory:",
                DbType = SqlSugar.DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute
            });
            CreateSchema();
            Db.Insertable(new SysUserEntity
            {
                Id = 1,
                UserName = "apple-user",
                PasswordHash = "unused",
                PointBalance = initialBalance,
                Status = 1,
                CreatedAt = DateTime.Now
            }).ExecuteCommand();
            var package = new PointRechargePackageEntity
            {
                PackageCode = packageCode,
                Name = "Credits 100",
                Points = packagePoints,
                ValidityDays = validityDays,
                PriceAmount = 6,
                Currency = "CNY",
                Status = packageStatus,
                CreatedAt = DateTime.Now
            };
            package.Id = Db.Insertable(package).ExecuteReturnBigIdentity();
            Db.Insertable(new AppleIapProductEntity
            {
                PackageId = package.Id,
                PackageCode = package.PackageCode,
                AppleProductId = ProductId,
                ProductType = "consumable",
                Points = applePoints ?? packagePoints,
                Environment = "Production",
                Status = 1,
                CreatedAt = DateTime.Now
            }).ExecuteCommand();

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(value => value.UserId).Returns(1);
            AppStoreClient = new Mock<IAppleAppStoreClient>();
            AppStoreClient.Setup(client => client.GetTransactionAsync(
                    TransactionId,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AppleTransactionLookupResult("purchase-transaction", "Production"));
            ConfigureTransaction("purchase-transaction", revocationDate: null, new string('A', 64));
            var tokenService = new Mock<IAppleAppAccountTokenService>();
            tokenService.Setup(service => service.GetForUser(1)).Returns(AccountToken);
            var options = Options.Create(new AppleAppStoreOptions
            {
                Enabled = true,
                BundleId = BundleId
            });
            Service = new AppleIapService(
                Db,
                currentUser.Object,
                AppStoreClient.Object,
                _jwsVerifier.Object,
                tokenService.Object,
                options,
                NullLogger<AppleIapService>.Instance);
        }

        public SqlSugarClient Db { get; }

        public Mock<IAppleAppStoreClient> AppStoreClient { get; }

        public AppleIapService Service { get; }

        public SysUserEntity User => Db.Queryable<SysUserEntity>().Single(user => user.Id == 1);

        public Task FulfillAsync() => Service.FulfillAsync(
            new FulfillAppleTransactionRequest
            {
                TransactionId = TransactionId,
                ProductId = ProductId,
                AppAccountToken = AccountToken
            },
            Guid.NewGuid().ToString("D"),
            default);

        public void ConfigureRefundNotification(string notificationUuid, string outerHash)
        {
            ConfigureOuterNotification(
                "refund-notification",
                notificationUuid,
                "REFUND",
                outerHash,
                includeTransaction: true);
            ConfigureTransaction(
                "refund-transaction",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                new string('F', 64));
        }

        public void ConfigureTestNotification(string notificationUuid, string payload, string hash) =>
            ConfigureOuterNotification(payload, notificationUuid, "TEST", hash, includeTransaction: false);

        public void ConfigureOuterNotification(
            string payload,
            string notificationUuid,
            string notificationType,
            string hash,
            bool includeTransaction)
        {
            var signedTransaction = includeTransaction
                ? ",\"signedTransactionInfo\":\"refund-transaction\""
                : string.Empty;
            var json = $$"""
                {
                  "notificationUUID": "{{notificationUuid}}",
                  "notificationType": "{{notificationType}}",
                  "data": {
                    "environment": "Production",
                    "bundleId": "{{BundleId}}"{{signedTransaction}}
                  }
                }
                """;
            _jwsVerifier.Setup(verifier => verifier.Verify(payload))
                .Returns(() => new AppleVerifiedJws(JsonDocument.Parse(json), hash));
        }

        private void ConfigureTransaction(string payload, long? revocationDate, string hash)
        {
            var revocation = revocationDate.HasValue
                ? $",\"revocationDate\":{revocationDate.Value}"
                : string.Empty;
            var json = $$"""
                {
                  "transactionId": "{{TransactionId}}",
                  "originalTransactionId": "{{TransactionId}}",
                  "productId": "{{ProductId}}",
                  "bundleId": "{{BundleId}}",
                  "environment": "Production",
                  "appAccountToken": "{{AccountToken}}",
                  "type": "Consumable",
                  "purchaseDate": {{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}},
                  "quantity": 1{{revocation}}
                }
                """;
            _jwsVerifier.Setup(verifier => verifier.Verify(payload))
                .Returns(() => new AppleVerifiedJws(JsonDocument.Parse(json), hash));
        }

        private void CreateSchema()
        {
            Db.Ado.ExecuteCommand("""
                CREATE TABLE sys_user (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_name TEXT NOT NULL,
                    nick_name TEXT NULL,
                    password_hash TEXT NOT NULL,
                    salt TEXT NULL,
                    email TEXT NULL,
                    phone TEXT NULL,
                    avatar_url TEXT NULL,
                    signature TEXT NULL,
                    point_balance INTEGER NOT NULL DEFAULT 0,
                    status INTEGER NOT NULL DEFAULT 1,
                    is_super_admin INTEGER NOT NULL DEFAULT 0,
                    last_login_time TEXT NULL,
                    last_login_ip TEXT NULL,
                    remark TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE point_recharge_package (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    package_code TEXT NOT NULL,
                    name TEXT NOT NULL,
                    description TEXT NULL,
                    points INTEGER NOT NULL,
                    repeat_points INTEGER NULL,
                    price_amount NUMERIC NOT NULL,
                    currency TEXT NOT NULL,
                    validity_days INTEGER NULL,
                    bonus_percent INTEGER NOT NULL DEFAULT 0,
                    badge_code TEXT NULL,
                    benefits_json TEXT NULL,
                    purchase_url TEXT NULL,
                    is_featured INTEGER NOT NULL DEFAULT 0,
                    sort INTEGER NOT NULL DEFAULT 0,
                    status INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE apple_iap_product (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    package_id INTEGER NOT NULL,
                    package_code TEXT NOT NULL,
                    apple_product_id TEXT NOT NULL,
                    product_type TEXT NOT NULL,
                    points INTEGER NOT NULL,
                    environment TEXT NOT NULL,
                    status INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE apple_transaction (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    transaction_id TEXT NOT NULL UNIQUE,
                    idempotency_key_hash TEXT NOT NULL,
                    request_fingerprint TEXT NOT NULL,
                    original_transaction_id TEXT NOT NULL,
                    user_id INTEGER NOT NULL,
                    product_id TEXT NOT NULL,
                    package_id INTEGER NOT NULL,
                    order_no TEXT NOT NULL,
                    environment TEXT NOT NULL,
                    app_account_token TEXT NOT NULL,
                    points INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    signed_transaction_hash TEXT NOT NULL,
                    purchase_date TEXT NOT NULL,
                    revocation_date TEXT NULL,
                    fulfilled_at TEXT NULL,
                    refunded_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );
                CREATE TABLE apple_server_notification (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    notification_uuid TEXT NOT NULL UNIQUE,
                    notification_type TEXT NOT NULL,
                    subtype TEXT NULL,
                    environment TEXT NOT NULL,
                    transaction_id TEXT NULL,
                    signed_payload_hash TEXT NOT NULL,
                    status TEXT NOT NULL,
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    failure_message TEXT NULL,
                    received_at TEXT NOT NULL,
                    processed_at TEXT NULL
                );
                CREATE TABLE apple_iap_debt (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    transaction_id TEXT NOT NULL UNIQUE,
                    points_owed INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );
                CREATE TABLE sys_user_point_detail (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    change_points INTEGER NOT NULL,
                    balance_after INTEGER NOT NULL,
                    change_type TEXT NOT NULL,
                    source TEXT NOT NULL,
                    business_key TEXT NULL,
                    remark TEXT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE sys_user_point_bucket (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    source TEXT NOT NULL,
                    business_key TEXT NOT NULL,
                    granted_points INTEGER NOT NULL,
                    remaining_points INTEGER NOT NULL,
                    expired_points INTEGER NOT NULL DEFAULT 0,
                    expires_at TEXT NULL,
                    spend_priority INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );
                CREATE TABLE ai_image_task (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    site_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    source_prompt_id INTEGER NULL,
                    prompt TEXT NOT NULL,
                    negative_prompt TEXT NULL,
                    prompt_policy_version INTEGER NOT NULL DEFAULT 0,
                    prompt_checked_at TEXT NULL,
                    model_name TEXT NULL,
                    model_code TEXT NULL,
                    size_contract_version TEXT NULL,
                    size_mode TEXT NULL,
                    requested_size TEXT NULL,
                    requested_width INTEGER NULL,
                    requested_height INTEGER NULL,
                    output_width INTEGER NULL,
                    output_height INTEGER NULL,
                    output_size TEXT NULL,
                    output_mime_type TEXT NULL,
                    model_release_id INTEGER NULL,
                    price_id INTEGER NULL,
                    price_release_id INTEGER NULL,
                    unit_point_cost INTEGER NULL,
                    image_count INTEGER NOT NULL,
                    completed_image_count INTEGER NOT NULL DEFAULT 0,
                    idempotency_key TEXT NOT NULL,
                    request_fingerprint TEXT NOT NULL,
                    point_cost INTEGER NOT NULL,
                    billing_status INTEGER NOT NULL DEFAULT 0,
                    refunded_points INTEGER NULL,
                    resolution_code TEXT NULL,
                    quality_code TEXT NOT NULL,
                    aspect_ratio_code TEXT NULL,
                    width INTEGER NOT NULL,
                    height INTEGER NOT NULL,
                    size TEXT NOT NULL,
                    quality TEXT NOT NULL,
                    reference_image_urls TEXT NULL,
                    mask_image_url TEXT NULL,
                    result_urls TEXT NULL,
                    status INTEGER NOT NULL DEFAULT 0,
                    error_message TEXT NULL,
                    failure_code TEXT NULL,
                    failure_stage TEXT NULL,
                    retryable INTEGER NULL,
                    claim_epoch INTEGER NOT NULL DEFAULT 0,
                    claim_token_hash TEXT NULL,
                    lease_expires_at TEXT NULL,
                    heartbeat_at TEXT NULL,
                    started_at TEXT NULL,
                    completed_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0,
                    UNIQUE (user_id, idempotency_key)
                );
                CREATE TABLE sys_user_point_bucket_usage (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bucket_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    business_key TEXT NOT NULL,
                    used_points INTEGER NOT NULL,
                    refunded_points INTEGER NOT NULL DEFAULT 0,
                    deferred_clawback_points INTEGER NOT NULL DEFAULT 0,
                    deferred_clawback_business_key TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );
                CREATE TABLE sys_user_membership_entitlement (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    tier_code TEXT NOT NULL,
                    source TEXT NOT NULL,
                    business_key TEXT NOT NULL UNIQUE,
                    starts_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    status TEXT NOT NULL,
                    revoked_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );
                """);
        }

        public void Dispose() => Db.Dispose();
    }
}
