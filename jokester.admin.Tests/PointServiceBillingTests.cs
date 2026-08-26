using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Domain.Entities;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class PointServiceBillingTests
{
    private const long UserId = 1;

    [Fact]
    public async Task ReserveImageTaskAsync_UsesMonthlyBucketBeforeHistoricalPermanentBalance()
    {
        using var context = new TestContext(pointBalance: 1_000);
        var monthly = context.AddBucket(
            points: 600,
            expiresAt: DateTime.Now.AddDays(30),
            spendPriority: 0,
            source: "recharge");

        var result = await context.Service.ReserveImageTaskAsync(
            CreateTask(pointCost: 300, key: "monthly-first"),
            "gpt-image-2",
            "1k",
            "med",
            default);
        var balance = await context.Service.GetBalanceAsync(default);

        Assert.True(result.Created);
        Assert.Equal(700, context.GetUser().PointBalance);
        Assert.Equal(300, context.GetBucket(monthly.Id).RemainingPoints);
        Assert.Equal(400, balance.PermanentPoints);
        Assert.Equal(300, balance.ExpiringPoints);
        Assert.Equal(300, balance.NextExpiringPoints);
        Assert.NotNull(balance.NextExpireAt);
        Assert.Equal(DateTimeKind.Utc, balance.NextExpireAt!.Value.Kind);
        var usage = Assert.Single(context.GetUsages());
        Assert.Equal(monthly.Id, usage.BucketId);
        Assert.Equal(300, usage.UsedPoints);
        Assert.Equal(0, usage.RefundedPoints);
    }

    [Fact]
    public async Task ReserveImageTaskAsync_SpansExpiringBucketsThenUsesPermanentBalance()
    {
        using var context = new TestContext(pointBalance: 1_000);
        var earlierMonthly = context.AddBucket(
            points: 100,
            expiresAt: DateTime.Now.AddDays(10),
            spendPriority: 0,
            source: "recharge");
        var laterMonthly = context.AddBucket(
            points: 80,
            expiresAt: DateTime.Now.AddDays(20),
            spendPriority: 0,
            source: "recharge");

        await context.Service.ReserveImageTaskAsync(
            CreateTask(pointCost: 250, key: "cross-buckets"),
            "gpt-image-2",
            "1k",
            "med",
            default);

        Assert.Equal(750, context.GetUser().PointBalance);
        Assert.Equal(0, context.GetBucket(earlierMonthly.Id).RemainingPoints);
        Assert.Equal(0, context.GetBucket(laterMonthly.Id).RemainingPoints);
        var usages = context.GetUsages().OrderBy(x => x.BucketId).ToArray();
        Assert.Equal(2, usages.Length);
        Assert.Equal(180, usages.Sum(x => x.UsedPoints));
        Assert.Contains(usages, x => x.BucketId == earlierMonthly.Id && x.UsedPoints == 100);
        Assert.Contains(usages, x => x.BucketId == laterMonthly.Id && x.UsedPoints == 80);
    }

    [Fact]
    public async Task GetBalanceAsync_ExpiresBucketOnceAndRemovesItFromAggregateBalance()
    {
        using var context = new TestContext(pointBalance: 1_000);
        var expired = context.AddBucket(
            points: 300,
            expiresAt: DateTime.Now.AddMinutes(-1),
            spendPriority: 0,
            source: "recharge");

        var first = await context.Service.GetBalanceAsync(default);
        var second = await context.Service.GetBalanceAsync(default);

        Assert.Equal(700, first.AvailablePoints);
        Assert.Equal(700, first.PermanentPoints);
        Assert.Equal(0, first.ExpiringPoints);
        Assert.Equal(700, second.AvailablePoints);
        Assert.Equal(700, context.GetUser().PointBalance);
        var expiredBucket = context.GetBucket(expired.Id);
        Assert.Equal(0, expiredBucket.RemainingPoints);
        Assert.Equal(300, expiredBucket.ExpiredPoints);
        var expiration = Assert.Single(context.GetDetails(), x => x.ChangeType == "expire");
        Assert.Equal(-300, expiration.ChangePoints);
        Assert.Equal(700, expiration.BalanceAfter);
    }

    [Fact]
    public async Task GetBalanceAsync_CountsTrackedPermanentPackageAsPermanentPoints()
    {
        using var context = new TestContext(pointBalance: 1_000);
        context.AddBucket(
            points: 400,
            expiresAt: null,
            spendPriority: 200,
            source: "recharge");

        var result = await context.Service.GetBalanceAsync(default);

        Assert.Equal(1_000, result.AvailablePoints);
        Assert.Equal(1_000, result.PermanentPoints);
        Assert.Equal(0, result.ExpiringPoints);
        Assert.Null(result.NextExpireAt);
    }

    [Fact]
    public async Task ReserveImageTaskAsync_ExpiresBucketsBeforeReservationAndRetryIsIdempotent()
    {
        using var context = new TestContext(pointBalance: 1_000);
        var expired = context.AddBucket(
            points: 300,
            expiresAt: DateTime.Now.AddMinutes(-1),
            spendPriority: 0,
            source: "recharge");
        var active = context.AddBucket(
            points: 200,
            expiresAt: DateTime.Now.AddDays(30),
            spendPriority: 0,
            source: "recharge");

        var first = await context.Service.ReserveImageTaskAsync(
            CreateTask(pointCost: 100, key: "expire-before-reserve"),
            "gpt-image-2",
            "1k",
            "med",
            default);
        var retry = await context.Service.ReserveImageTaskAsync(
            CreateTask(pointCost: 100, key: "expire-before-reserve"),
            "gpt-image-2",
            "1k",
            "med",
            default);

        Assert.True(first.Created);
        Assert.False(retry.Created);
        Assert.Equal(first.TaskId, retry.TaskId);
        Assert.Equal(600, context.GetUser().PointBalance);
        Assert.Equal(0, context.GetBucket(expired.Id).RemainingPoints);
        Assert.Equal(100, context.GetBucket(active.Id).RemainingPoints);
        Assert.Single(context.GetDetails(), x => x.ChangeType == "expire");
        Assert.Single(context.GetDetails(), x => x.ChangeType == "consume");
        var usage = Assert.Single(context.GetUsages());
        Assert.Equal(active.Id, usage.BucketId);
        Assert.Equal(100, usage.UsedPoints);
    }

    [Fact]
    public async Task SettleImageTaskAsync_WhenTaskFails_RestoresOriginalBucketWithoutExtendingExpiry()
    {
        using var context = new TestContext(pointBalance: 1_000);
        var monthly = context.AddBucket(
            points: 500,
            expiresAt: DateTime.Now.AddDays(15),
            spendPriority: 0,
            source: "recharge");
        var originalExpiry = context.GetBucket(monthly.Id).ExpiresAt;
        var reservation = await context.Service.ReserveImageTaskAsync(
            CreateTask(pointCost: 300, key: "refund-original-bucket"),
            "gpt-image-2",
            "1k",
            "med",
            default);

        var settlement = await context.Service.SettleImageTaskAsync(
            reservation.TaskId,
            finalStatus: 2,
            resultUrls: null,
            errorMessage: "provider failed",
            completedImageCount: 0,
            default);
        var retry = await context.Service.SettleImageTaskAsync(
            reservation.TaskId,
            finalStatus: 2,
            resultUrls: null,
            errorMessage: "provider failed",
            completedImageCount: 0,
            default);

        Assert.True(settlement.Transitioned);
        Assert.Equal(300, settlement.RefundedPoints);
        Assert.False(retry.Transitioned);
        Assert.Equal(0, retry.RefundedPoints);
        Assert.Equal(1_000, context.GetUser().PointBalance);
        var restoredBucket = context.GetBucket(monthly.Id);
        Assert.Equal(500, restoredBucket.RemainingPoints);
        Assert.Equal(originalExpiry, restoredBucket.ExpiresAt);
        var usage = Assert.Single(context.GetUsages());
        Assert.Equal(300, usage.UsedPoints);
        Assert.Equal(300, usage.RefundedPoints);
        Assert.Single(context.GetDetails(), x => x.ChangeType == "refund");
    }

    [Fact]
    public async Task SettleImageTaskAsync_PartialRefundRestoresPermanentPointsBeforeMonthlyBucket()
    {
        using var context = new TestContext(pointBalance: 200);
        var monthly = context.AddBucket(
            points: 100,
            expiresAt: DateTime.Now.AddDays(15),
            spendPriority: 0,
            source: "recharge");
        var task = CreateTask(pointCost: 200, key: "partial-refund-order");
        task.ImageCount = 2;
        var reservation = await context.Service.ReserveImageTaskAsync(
            task,
            "gpt-image-2",
            "1k",
            "med",
            default);

        var settlement = await context.Service.SettleImageTaskAsync(
            reservation.TaskId,
            finalStatus: 2,
            resultUrls: "[\"/api/media/ai/one.png\"]",
            errorMessage: "one image failed",
            completedImageCount: 1,
            default);

        Assert.Equal(100, settlement.RefundedPoints);
        Assert.Equal(100, context.GetUser().PointBalance);
        Assert.Equal(0, context.GetBucket(monthly.Id).RemainingPoints);
        var usage = Assert.Single(context.GetUsages());
        Assert.Equal(100, usage.UsedPoints);
        Assert.Equal(0, usage.RefundedPoints);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettleImageTaskAsync_AppleRefundedReservationFailureDoesNotRestoreRevokedGrantPoints(
        bool expiringGrant)
    {
        using var context = new TestContext(pointBalance: 100);
        var monthly = context.AddBucket(
            points: 100,
            expiresAt: expiringGrant ? DateTime.Now.AddDays(30) : null,
            spendPriority: expiringGrant ? 0 : 200,
            source: "apple_iap");
        var reservation = await context.Service.ReserveImageTaskAsync(
            CreateTask(pointCost: 60, key: "apple-refund-failure"),
            "gpt-image-2",
            "1k",
            "med",
            default);
        const string appleRefundKey = "apple:2000000123456789:refund";
        var imageUsage = Assert.Single(context.GetUsages());
        context.Db.Updateable<PointBucketUsageEntity>()
            .SetColumns(value => new PointBucketUsageEntity
            {
                DeferredClawbackPoints = 60,
                DeferredClawbackBusinessKey = appleRefundKey
            })
            .Where(value => value.Id == imageUsage.Id)
            .ExecuteCommand();
        context.Db.Updateable<PointBucketEntity>()
            .SetColumns(value => value.RemainingPoints == 0)
            .Where(value => value.Id == monthly.Id)
            .ExecuteCommand();
        context.Db.Updateable<SysUserEntity>()
            .SetColumns(value => value.PointBalance == 0)
            .Where(value => value.Id == UserId)
            .ExecuteCommand();
        context.Db.Insertable(new PointBucketUsageEntity
        {
            BucketId = monthly.Id,
            UserId = UserId,
            BusinessKey = appleRefundKey,
            UsedPoints = 40,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();

        var settlement = await context.Service.SettleImageTaskAsync(
            reservation.TaskId,
            finalStatus: 2,
            resultUrls: null,
            errorMessage: "provider failed after Apple refund",
            completedImageCount: 0,
            default);

        Assert.True(settlement.Transitioned);
        Assert.Equal(60, settlement.RefundedPoints);
        Assert.Equal(0, context.GetUser().PointBalance);
        Assert.Equal(0, context.GetBucket(monthly.Id).RemainingPoints);
        imageUsage = context.GetUsages().Single(value => value.Id == imageUsage.Id);
        Assert.Equal(0, imageUsage.RefundedPoints);
        Assert.Equal(60, imageUsage.DeferredClawbackPoints);
        var refundDetail = Assert.Single(context.GetDetails(), value => value.Source == "image_refund");
        Assert.Equal(0, refundDetail.ChangePoints);
        Assert.Contains("Apple", refundDetail.Remark);
        Assert.Empty(context.Db.Queryable<AppleIapDebtEntity>().ToList());
    }

    [Fact]
    public async Task SettleImageTaskAsync_AppleRefundedReservationSuccessAppliesDeferredClawback()
    {
        using var context = new TestContext(pointBalance: 125);
        var monthly = context.AddBucket(
            points: 100,
            expiresAt: DateTime.Now.AddDays(30),
            spendPriority: 0,
            source: "apple_iap");
        var reservation = await context.Service.ReserveImageTaskAsync(
            CreateTask(pointCost: 60, key: "apple-refund-success"),
            "gpt-image-2",
            "1k",
            "med",
            default);
        const string appleRefundKey = "apple:2000000123456789:refund";
        var imageUsage = Assert.Single(context.GetUsages());
        context.Db.Updateable<PointBucketUsageEntity>()
            .SetColumns(value => new PointBucketUsageEntity
            {
                DeferredClawbackPoints = 60,
                DeferredClawbackBusinessKey = appleRefundKey
            })
            .Where(value => value.Id == imageUsage.Id)
            .ExecuteCommand();
        context.Db.Updateable<PointBucketEntity>()
            .SetColumns(value => value.RemainingPoints == 0)
            .Where(value => value.Id == monthly.Id)
            .ExecuteCommand();
        context.Db.Updateable<SysUserEntity>()
            .SetColumns(value => value.PointBalance == 25)
            .Where(value => value.Id == UserId)
            .ExecuteCommand();
        context.Db.Insertable(new PointBucketUsageEntity
        {
            BucketId = monthly.Id,
            UserId = UserId,
            BusinessKey = appleRefundKey,
            UsedPoints = 40,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();

        var settlement = await context.Service.SettleImageTaskAsync(
            reservation.TaskId,
            finalStatus: 1,
            resultUrls: "[\"/api/media/ai/success.png\"]",
            errorMessage: null,
            completedImageCount: 1,
            default);
        var retry = await context.Service.SettleImageTaskAsync(
            reservation.TaskId,
            finalStatus: 1,
            resultUrls: "[\"/api/media/ai/success.png\"]",
            errorMessage: null,
            completedImageCount: 1,
            default);

        Assert.True(settlement.Transitioned);
        Assert.Equal(0, settlement.RefundedPoints);
        Assert.False(retry.Transitioned);
        Assert.Equal(0, retry.RefundedPoints);
        Assert.Equal(0, context.GetUser().PointBalance);
        var debt = Assert.Single(context.Db.Queryable<AppleIapDebtEntity>().ToList());
        Assert.Equal("2000000123456789", debt.TransactionId);
        Assert.Equal(35, debt.PointsOwed);
        Assert.Equal("open", debt.Status);
        var deferredDetail = Assert.Single(
            context.GetDetails(),
            value => value.BusinessKey == $"apple:{debt.TransactionId}:task:{reservation.TaskId}");
        Assert.Equal(-25, deferredDetail.ChangePoints);
    }

    [Fact]
    public async Task SettleImageTaskAsync_PartialFailureCreditsPermanentRefundBeforeDeferredAppleClawback()
    {
        using var context = new TestContext(pointBalance: 200);
        var monthly = context.AddBucket(
            points: 100,
            expiresAt: DateTime.Now.AddDays(30),
            spendPriority: 0,
            source: "apple_iap");
        var task = CreateTask(pointCost: 200, key: "apple-refund-partial-cross-source");
        task.ImageCount = 2;
        var reservation = await context.Service.ReserveImageTaskAsync(
            task,
            "gpt-image-2",
            "1k",
            "med",
            default);
        const string appleRefundKey = "apple:2000000123456789:refund";
        var imageUsage = Assert.Single(context.GetUsages());
        context.Db.Updateable<PointBucketUsageEntity>()
            .SetColumns(value => new PointBucketUsageEntity
            {
                DeferredClawbackPoints = 100,
                DeferredClawbackBusinessKey = appleRefundKey
            })
            .Where(value => value.Id == imageUsage.Id)
            .ExecuteCommand();

        var settlement = await context.Service.SettleImageTaskAsync(
            reservation.TaskId,
            finalStatus: 2,
            resultUrls: "[\"/api/media/ai/partial-success.png\"]",
            errorMessage: "one image failed after Apple refund",
            completedImageCount: 1,
            default);

        Assert.True(settlement.Transitioned);
        Assert.Equal(100, settlement.RefundedPoints);
        Assert.Equal(0, context.GetUser().PointBalance);
        Assert.Equal(0, context.GetBucket(monthly.Id).RemainingPoints);
        imageUsage = context.GetUsages().Single(value => value.Id == imageUsage.Id);
        Assert.Equal(0, imageUsage.RefundedPoints);
        Assert.Equal(100, imageUsage.DeferredClawbackPoints);
        Assert.Empty(context.Db.Queryable<AppleIapDebtEntity>().ToList());

        var refundDetail = Assert.Single(
            context.GetDetails(),
            value => value.BusinessKey == $"image:{reservation.TaskId}:refund");
        var clawbackDetail = Assert.Single(
            context.GetDetails(),
            value => value.BusinessKey == $"apple:2000000123456789:task:{reservation.TaskId}");
        Assert.Equal(100, refundDetail.ChangePoints);
        Assert.Equal(100, refundDetail.BalanceAfter);
        Assert.Equal(-100, clawbackDetail.ChangePoints);
        Assert.Equal(0, clawbackDetail.BalanceAfter);
        Assert.True(refundDetail.Id < clawbackDetail.Id);
    }

    [Fact]
    public async Task SettleImageTaskAsync_WhenOriginalBucketAlreadyExpired_RefundExpiresAgainWithoutConflict()
    {
        using var context = new TestContext(pointBalance: 1_000);
        var monthly = context.AddBucket(
            points: 500,
            expiresAt: DateTime.Now.AddDays(15),
            spendPriority: 0,
            source: "recharge");
        var reservation = await context.Service.ReserveImageTaskAsync(
            CreateTask(pointCost: 300, key: "refund-expired-bucket"),
            "gpt-image-2",
            "1k",
            "med",
            default);
        context.Db.Updateable<PointBucketEntity>()
            .SetColumns(x => x.ExpiresAt == DateTime.Now.AddMinutes(-1))
            .Where(x => x.Id == monthly.Id)
            .ExecuteCommand();
        await context.Service.GetBalanceAsync(default);

        var settlement = await context.Service.SettleImageTaskAsync(
            reservation.TaskId,
            finalStatus: 2,
            resultUrls: null,
            errorMessage: "provider failed after expiry",
            completedImageCount: 0,
            default);

        Assert.True(settlement.Transitioned);
        Assert.Equal(300, settlement.RefundedPoints);
        Assert.Equal(500, context.GetUser().PointBalance);
        var expiredBucket = context.GetBucket(monthly.Id);
        Assert.Equal(0, expiredBucket.RemainingPoints);
        Assert.Equal(500, expiredBucket.ExpiredPoints);
        Assert.Equal(300, Assert.Single(context.GetUsages()).RefundedPoints);
        var expirations = context.GetDetails().Where(x => x.ChangeType == "expire").ToArray();
        Assert.Equal(2, expirations.Length);
        Assert.Equal(2, expirations.Select(x => x.BusinessKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ReserveImageTaskAsync_UsesMonthlyBucketBeforeCurrentSignInBucket()
    {
        using var context = new TestContext(pointBalance: 300);
        var monthly = context.AddBucket(
            points: 300,
            expiresAt: DateTime.Now.AddDays(30),
            spendPriority: 0,
            source: "recharge");
        await context.Service.SignInAsync(default);
        var signIn = Assert.Single(context.Db.Queryable<PointBucketEntity>()
            .Where(x => x.Source == "sign_in")
            .ToList());

        await context.Service.ReserveImageTaskAsync(
            CreateTask(pointCost: 100, key: "monthly-before-sign-in"),
            "gpt-image-2",
            "1k",
            "med",
            default);

        Assert.Equal(225, context.GetUser().PointBalance);
        Assert.Equal(200, context.GetBucket(monthly.Id).RemainingPoints);
        Assert.Equal(25, context.GetBucket(signIn.Id).RemainingPoints);
        var usage = Assert.Single(context.GetUsages());
        Assert.Equal(monthly.Id, usage.BucketId);
        Assert.Equal(100, usage.UsedPoints);
    }

    [Fact]
    public async Task ReserveImageTaskAsync_UsesCurrentSignInBeforeTrackedPermanentPackage()
    {
        using var context = new TestContext(pointBalance: 300);
        var permanentPackage = context.AddBucket(
            points: 200,
            expiresAt: null,
            spendPriority: 200,
            source: "recharge");
        await context.Service.SignInAsync(default);
        var signIn = Assert.Single(context.Db.Queryable<PointBucketEntity>()
            .Where(x => x.Source == "sign_in")
            .ToList());

        await context.Service.ReserveImageTaskAsync(
            CreateTask(pointCost: 25, key: "sign-in-before-permanent-package"),
            "gpt-image-2",
            "1k",
            "med",
            default);

        Assert.Equal(300, context.GetUser().PointBalance);
        Assert.Equal(0, context.GetBucket(signIn.Id).RemainingPoints);
        Assert.Equal(200, context.GetBucket(permanentPackage.Id).RemainingPoints);
        var usage = Assert.Single(context.GetUsages());
        Assert.Equal(signIn.Id, usage.BucketId);
        Assert.Equal(25, usage.UsedPoints);
    }

    [Fact]
    public async Task VersionedBatchReservation_ReplaysWithoutChargingAndSettlesStablePerTaskFacts()
    {
        using var context = new TestContext(pointBalance: 100);
        context.AddBucket(100, null, 2, "recharge");
        var now = DateTime.Now;
        var release = new AiImageModelReleaseEntity
        {
            ModelCode = "gpt-image-2",
            ModelName = "GPT Image 2",
            CatalogVersion = "imgcat_billing_1",
            SizeContractVersion = AiImageCatalogService.SizeContractVersion,
            DefaultSizeMode = "explicit",
            Status = "published",
            CreatedAt = now,
            PublishedAt = now
        };
        release.Id = context.Db.Insertable(release).ExecuteReturnBigIdentity();
        context.Db.Insertable(new AiImageCurrentReleaseEntity
        {
            ModelCode = release.ModelCode,
            ModelReleaseId = release.Id,
            UpdatedAt = now
        }).ExecuteCommand();
        var price = new AiImageModelReleasePriceEntity
        {
            ModelReleaseId = release.Id,
            ModelCode = release.ModelCode,
            PricingMode = "auto",
            ResolutionCode = string.Empty,
            QualityCode = "med",
            Points = 15,
            PriceAmount = 0.15m,
            Currency = "CNY",
            Status = 1
        };
        price.Id = context.Db.Insertable(price).ExecuteReturnBigIdentity();

        var request = CreateVersionedRequest(release.Id, now);
        var tasks = Enumerable.Range(0, 2)
            .Select(index => CreateVersionedTask(release.Id, price.Id, index, now))
            .ToArray();
        var inputs = Enumerable.Range(0, 2)
            .Select(index => new AiImageTaskInputEntity
            {
                RequestTaskOrdinal = index,
                Role = "reference",
                InputOrdinal = 0,
                InputKind = "legacy_url",
                OwnerUserId = UserId,
                LegacyUrl = $"/api/media/ai/{UserId}/reference.png",
                CreatedAt = now
            })
            .ToArray();

        var reserved = await context.Service.ReserveVersionedImageTasksAsync(
            request,
            tasks,
            inputs,
            price.Id,
            default);

        Assert.True(reserved.Created);
        Assert.Equal(tasks.Select(x => x.Id), reserved.TaskIds);
        Assert.Equal(70, context.GetUser().PointBalance);
        Assert.Equal(2, context.Db.Queryable<AiImageRequestTaskEntity>().Count());
        Assert.Equal(tasks.Select(x => x.Id), context.Db.Queryable<AiImageRequestTaskEntity>()
            .OrderBy(x => x.TaskOrdinal).Select(x => x.TaskId).ToList());
        Assert.Equal(2, context.Db.Queryable<AiImageTaskInputEntity>().Count());
        Assert.Equal(2, context.Db.Queryable<AiImageTaskOutboxEntity>().Count());
        Assert.All(context.Db.Queryable<AiImageTaskEntity>().OrderBy(x => x.Id).ToList(), task =>
        {
            Assert.Null(task.ResolutionCode);
            Assert.Null(task.AspectRatioCode);
            Assert.Equal("auto", task.RequestedSize);
        });

        var replay = await context.Service.ReserveVersionedImageTasksAsync(
            CreateVersionedRequest(release.Id, now.AddMinutes(1)),
            Enumerable.Range(0, 2).Select(index => CreateVersionedTask(release.Id, price.Id, index, now)).ToArray(),
            [],
            price.Id,
            default);

        Assert.False(replay.Created);
        Assert.Equal(reserved.TaskIds, replay.TaskIds);
        Assert.Equal(70, context.GetUser().PointBalance);
        Assert.Equal(2, context.GetDetails().Count(x => x.Source == "image_generate"));

        var claimHashes = new[] { new string('a', 64), new string('b', 64) };
        for (var index = 0; index < tasks.Length; index++)
        {
            context.Db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 3,
                    ClaimEpoch = 1,
                    ClaimTokenHash = claimHashes[index],
                    LeaseExpiresAt = now.AddMinutes(5)
                })
                .Where(x => x.Id == tasks[index].Id)
                .ExecuteCommand();
            context.Db.Insertable(new AiImageProviderAttemptEntity
            {
                AttemptId = $"attempt{index:D2}".PadRight(32, '0'),
                TaskId = tasks[index].Id,
                ClaimEpoch = 1,
                ModelReleaseId = release.Id,
                UpstreamIdempotencyKey = tasks[index].IdempotencyKey,
                State = "inflight",
                StartedAt = now,
                Deadline = now.AddMinutes(5),
                ReconcileBy = now.AddMinutes(35)
            }).ExecuteCommand();
        }

        var success = await context.Service.SettleVersionedImageTaskAsync(
            tasks[0].Id,
            1,
            new VersionedImageTaskSettlement(
                "[\"/api/media/ai/1/success.png\"]",
                1536,
                1024,
                "1536x1024",
                "image/png",
                null,
                null,
                null,
                1,
                claimHashes[0],
                "attempt00".PadRight(32, '0'),
                "succeeded"),
            null,
            1,
            default);
        var failure = await context.Service.SettleVersionedImageTaskAsync(
            tasks[1].Id,
            2,
            new VersionedImageTaskSettlement(
                null,
                null,
                null,
                null,
                null,
                MachineErrorCodes.ProviderUnavailable,
                "provider",
                true,
                1,
                claimHashes[1],
                "attempt01".PadRight(32, '0'),
                "failed"),
            "图片生成服务暂时不可用，请稍后重试。",
            0,
            default);

        Assert.True(success.Transitioned);
        Assert.True(failure.Transitioned);
        Assert.Equal(15, failure.RefundedPoints);
        Assert.Equal(85, context.GetUser().PointBalance);
        var settledTasks = context.Db.Queryable<AiImageTaskEntity>().OrderBy(x => x.Id).ToList();
        Assert.Equal((1, 1, 0, 1536, 1024),
            (settledTasks[0].Status, settledTasks[0].BillingStatus, settledTasks[0].RefundedPoints,
                settledTasks[0].OutputWidth, settledTasks[0].OutputHeight));
        Assert.Equal((2, 3, 15, MachineErrorCodes.ProviderUnavailable, "provider", true),
            (settledTasks[1].Status, settledTasks[1].BillingStatus, settledTasks[1].RefundedPoints,
                settledTasks[1].FailureCode, settledTasks[1].FailureStage, settledTasks[1].Retryable));
        var result = Assert.Single(context.Db.Queryable<AiImageTaskResultEntity>().ToList());
        Assert.Equal((tasks[0].Id, 1536, 1024, "image/png"),
            (result.TaskId, result.Width, result.Height, result.MimeType));
        Assert.Equal(1, context.GetDetails().Count(x => x.Source == "image_refund"));
    }

    private static AiImageRequestEntity CreateVersionedRequest(long releaseId, DateTime createdAt) => new()
    {
        UserId = UserId,
        IdempotencyKeyHash = new string('c', 64),
        CanonicalPayloadHash = new string('d', 64),
        CanonicalizationVersion = AiImageCatalogService.SizeContractVersion,
        NormalizationProfile = "native-v1",
        SizeContractVersion = AiImageCatalogService.SizeContractVersion,
        ModelReleaseId = releaseId,
        AdmissionReservationId = "owner-token",
        AdmissionQuotaDate = createdAt.ToString("yyyyMMdd"),
        ReservedPointCost = 30,
        RequestedImageCount = 2,
        TaskCount = 2,
        LegacyBatchShape = "split-task-per-image",
        Status = "active",
        CreatedAt = createdAt
    };

    private static AiImageTaskEntity CreateVersionedTask(long releaseId, long priceId, int ordinal, DateTime createdAt) => new()
    {
        SiteId = 1,
        UserId = UserId,
        Prompt = "versioned billing test",
        ModelName = "gpt-image-2",
        ModelCode = "gpt-image-2",
        SizeContractVersion = AiImageCatalogService.SizeContractVersion,
        SizeMode = "auto",
        RequestedSize = "auto",
        ModelReleaseId = releaseId,
        PriceId = priceId,
        PriceReleaseId = releaseId,
        UnitPointCost = 15,
        ImageCount = 1,
        IdempotencyKey = $"task-key-{ordinal}",
        RequestFingerprint = new string('d', 64),
        PointCost = 15,
        BillingStatus = 0,
        RefundedPoints = 0,
        ResolutionCode = null,
        QualityCode = "med",
        AspectRatioCode = null,
        Width = 0,
        Height = 0,
        Size = "auto",
        Quality = "medium",
        Status = 0,
        CreatedAt = createdAt
    };

    private static AiImageTaskEntity CreateTask(int pointCost, string key) => new()
    {
        SiteId = 1,
        UserId = UserId,
        Prompt = "billing test",
        ModelName = "gpt-image-2",
        ImageCount = 1,
        IdempotencyKey = key,
        RequestFingerprint = $"fingerprint:{key}",
        PointCost = pointCost,
        ResolutionCode = "1k",
        QualityCode = "med",
        AspectRatioCode = "1:1",
        Width = 1024,
        Height = 1024,
        Size = "1024x1024",
        Quality = "medium",
        Status = 0,
        BillingStatus = 0,
        CreatedAt = DateTime.Now
    };

    private sealed class TestContext : IDisposable
    {
        private int _bucketSequence;

        public TestContext(int pointBalance)
        {
            SQLitePCL.Batteries_V2.Init();
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=:memory:",
                DbType = SqlSugar.DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute
            });
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

                CREATE TABLE sys_user_point_detail (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    change_points INTEGER NOT NULL,
                    balance_after INTEGER NOT NULL,
                    change_type TEXT NOT NULL,
                    source TEXT NOT NULL,
                    business_key TEXT NULL,
                    remark TEXT NULL,
                    created_at TEXT NOT NULL,
                    UNIQUE (business_key)
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
                    updated_at TEXT NULL,
                    UNIQUE (user_id, business_key)
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
                    updated_at TEXT NULL,
                    UNIQUE (bucket_id, business_key)
                );

                CREATE TABLE apple_iap_debt (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    transaction_id TEXT NOT NULL,
                    points_owed INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );

                CREATE TABLE ai_image_model_release (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    model_code TEXT NOT NULL,
                    model_name TEXT NOT NULL,
                    catalog_version TEXT NOT NULL,
                    size_contract_version TEXT NOT NULL,
                    default_size_mode TEXT NOT NULL,
                    status TEXT NOT NULL,
                    revoked_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    published_at TEXT NULL
                );

                CREATE TABLE ai_image_model_current_release (
                    model_code TEXT PRIMARY KEY,
                    model_release_id INTEGER NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE ai_image_model_release_price (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    model_release_id INTEGER NOT NULL,
                    model_code TEXT NOT NULL,
                    pricing_mode TEXT NOT NULL,
                    resolution_code TEXT NOT NULL,
                    quality_code TEXT NOT NULL,
                    points INTEGER NOT NULL,
                    price_amount NUMERIC NOT NULL,
                    currency TEXT NOT NULL,
                    sort INTEGER NOT NULL DEFAULT 0,
                    status INTEGER NOT NULL
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

                CREATE TABLE ai_image_request_idempotency (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    idempotency_key_hash TEXT NOT NULL,
                    canonical_payload_hash TEXT NOT NULL,
                    canonicalization_version TEXT NOT NULL,
                    normalization_profile TEXT NOT NULL,
                    size_contract_version TEXT NOT NULL,
                    model_release_id INTEGER NULL,
                    admission_reservation_id TEXT NULL,
                    admission_quota_date TEXT NULL,
                    reserved_point_cost INTEGER NOT NULL,
                    requested_image_count INTEGER NOT NULL,
                    task_count INTEGER NOT NULL,
                    legacy_batch_shape TEXT NOT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    UNIQUE (user_id, idempotency_key_hash)
                );

                CREATE TABLE ai_image_request_task (
                    request_id INTEGER NOT NULL,
                    task_ordinal INTEGER NOT NULL,
                    task_id INTEGER NOT NULL,
                    PRIMARY KEY (request_id, task_ordinal),
                    UNIQUE (task_id)
                );

                CREATE TABLE ai_image_task_input (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_id INTEGER NOT NULL,
                    role TEXT NOT NULL,
                    input_ordinal INTEGER NOT NULL,
                    input_kind TEXT NOT NULL,
                    asset_id TEXT NULL,
                    owner_user_id INTEGER NOT NULL,
                    storage_key TEXT NULL,
                    content_sha256 TEXT NULL,
                    legacy_url TEXT NULL,
                    created_at TEXT NOT NULL,
                    UNIQUE (task_id, role, input_ordinal)
                );

                CREATE TABLE ai_image_task_outbox (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    request_id INTEGER NOT NULL,
                    task_id INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    attempt_count INTEGER NOT NULL DEFAULT 0,
                    next_attempt_at TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL,
                    UNIQUE (request_id, task_id)
                );

                CREATE TABLE ai_image_provider_attempt (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    attempt_id TEXT NOT NULL UNIQUE,
                    task_id INTEGER NOT NULL,
                    claim_epoch INTEGER NOT NULL,
                    model_release_id INTEGER NULL,
                    release_route_id INTEGER NULL,
                    route_role TEXT NULL,
                    consent_provider_code TEXT NULL,
                    upstream_idempotency_key TEXT NOT NULL,
                    state TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    deadline TEXT NOT NULL,
                    reconcile_by TEXT NOT NULL,
                    completed_at TEXT NULL,
                    UNIQUE (task_id, claim_epoch)
                );

                CREATE TABLE ai_image_task_result (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_id INTEGER NOT NULL,
                    result_ordinal INTEGER NOT NULL,
                    url TEXT NOT NULL,
                    width INTEGER NOT NULL,
                    height INTEGER NOT NULL,
                    size TEXT NOT NULL,
                    mime_type TEXT NOT NULL,
                    is_quarantined INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    UNIQUE (task_id, result_ordinal)
                );

                CREATE TABLE prompt_library_metric_daily (
                    prompt_id INTEGER NOT NULL,
                    metric_date TEXT NOT NULL,
                    detail_view_count INTEGER NOT NULL DEFAULT 0,
                    copy_count INTEGER NOT NULL DEFAULT 0,
                    use_count INTEGER NOT NULL DEFAULT 0,
                    successful_generation_count INTEGER NOT NULL DEFAULT 0,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY (prompt_id, metric_date)
                );
                """);

            Db.Insertable(new SysUserEntity
            {
                Id = UserId,
                UserName = "billing-user",
                PasswordHash = "unused",
                PointBalance = pointBalance,
                Status = 1,
                CreatedAt = DateTime.Now
            }).ExecuteCommand();

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(UserId);
            Service = new PointService(Db, currentUser.Object);
        }

        public SqlSugarClient Db { get; }

        public PointService Service { get; }

        public PointBucketEntity AddBucket(
            int points,
            DateTime? expiresAt,
            int spendPriority,
            string source)
        {
            var sequence = ++_bucketSequence;
            var entity = new PointBucketEntity
            {
                UserId = UserId,
                Source = source,
                BusinessKey = $"test:{source}:{sequence}",
                GrantedPoints = points,
                RemainingPoints = points,
                ExpiresAt = expiresAt,
                SpendPriority = spendPriority,
                CreatedAt = DateTime.Now.AddMinutes(sequence)
            };
            entity.Id = Db.Insertable(entity).ExecuteReturnBigIdentity();
            return entity;
        }

        public SysUserEntity GetUser() => Db.Queryable<SysUserEntity>().Single(x => x.Id == UserId);

        public PointBucketEntity GetBucket(long bucketId) =>
            Db.Queryable<PointBucketEntity>().Single(x => x.Id == bucketId);

        public List<PointBucketUsageEntity> GetUsages() =>
            Db.Queryable<PointBucketUsageEntity>().OrderBy(x => x.Id).ToList();

        public List<UserPointDetailEntity> GetDetails() =>
            Db.Queryable<UserPointDetailEntity>().OrderBy(x => x.Id).ToList();

        public void Dispose() => Db.Dispose();
    }
}
