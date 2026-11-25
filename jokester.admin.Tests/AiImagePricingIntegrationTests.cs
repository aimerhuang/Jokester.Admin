using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.DTOs.NanoBananaImages;
using jokester.admin.Application.Models.AiPromptFilter;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Controllers;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Tests;

public sealed class AiImagePricingIntegrationTests
{
    // Use an isolated database initialized by jokester.admin.sql. Each case creates its own identity and model.
    private sealed class PricingDatabaseFactAttribute : FactAttribute
    {
        public PricingDatabaseFactAttribute(bool redis = false)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JOKESTER_TEST_PRICING_MYSQL")))
                Skip = "Set JOKESTER_TEST_PRICING_MYSQL to the isolated, initialized pricing test database.";
            else if (redis && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JOKESTER_TEST_REDIS")))
                Skip = "Set JOKESTER_TEST_REDIS to the isolated quota test server.";
        }
    }

    private static SqlSugarClient Open() => new(new ConnectionConfig
    {
        DbType = SqlSugar.DbType.MySql, IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
        ConnectionString = Environment.GetEnvironmentVariable("JOKESTER_TEST_PRICING_MYSQL")!
    });

    private static long CreateUser(SqlSugarClient db) => db.Insertable(new SysUserEntity
    {
        UserName = "viptest_" + Guid.NewGuid().ToString("N"), PasswordHash = "unused", Status = 1,
        PointBalance = 500, CreatedAt = DateTime.Now
    }).ExecuteReturnBigIdentity();

    [PricingDatabaseFact]
    public async Task BatchReservation_SerializesWithRepricingAndDoesNotLockGlobalSettings()
    {
        using var billing = Open();
        using var writer = Open();
        using var settings = Open();
        var model = "vip-test-" + Guid.NewGuid().ToString("N");
        var userId = CreateUser(billing);
        var site = billing.Ado.GetLong("SELECT id FROM sys_site WHERE site_code='ai_image'");
        var id = billing.Insertable(new AiImagePointPriceEntity
        {
            ModelCode = model, ResolutionCode = "1k", QualityCode = "med", Points = 40, Status = 1,
            VipPriceMode = "follow", CreatedAt = DateTime.Now
        }).ExecuteReturnBigIdentity();
        var pointService = new PointService(billing, Mock.Of<ICurrentUser>(x => x.UserId == userId));
        AiImageTaskEntity TaskRow() => new()
        {
            SiteId = site, UserId = userId, ModelName = model, Prompt = "test", ImageCount = 1, ResolutionCode = "1k",
            QualityCode = "med", IdempotencyKey = Guid.NewGuid().ToString("N"), RequestFingerprint = new string('a', 64),
            PointCost = 999, CreatedAt = DateTime.Now
        };
        var original = new[] { TaskRow(), TaskRow() };
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reservation = pointService.ReserveImageTasksAsync(original, model, "1k", "med", default, 40, async total =>
        {
            Assert.Equal(80, total);
            locked.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
        });
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var reprice = writer.Updateable<AiImagePointPriceEntity>().SetColumns(x => x.Points == 30)
            .Where(x => x.Id == id).ExecuteCommandAsync();
        try
        {
            await Task.Delay(120);
            Assert.False(reprice.IsCompleted);
            // NOWAIT fails immediately if the billing transaction contends on the settings row.
            await settings.Ado.GetLongAsync("SELECT id FROM ai_image_pricing_setting WHERE id=1 FOR UPDATE NOWAIT");
        }
        finally { release.TrySetResult(); }
        await reservation;
        await reprice;
        Assert.All(original, task => Assert.Equal(40, task.UnitPointCost));
        var afterEdit = new[] { TaskRow(), TaskRow() };
        await pointService.ReserveImageTasksAsync(afterEdit, model, "1k", "med", default, 40);
        Assert.All(afterEdit, task => Assert.Equal(30, task.UnitPointCost));
        Assert.Equal(360, (await billing.Queryable<SysUserEntity>().SingleAsync(x => x.Id == userId)).PointBalance);
    }

    [PricingDatabaseFact(redis: true)]
    public async Task NanoAdmission_RepricesAfterRedisAndRetriesRejectedKeyAtConfirmedPrice()
    {
        using var db = Open();
        using var writer = Open();
        using var http = new HttpClient();
        await using var redis = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("JOKESTER_TEST_REDIS")!);
        var userId = CreateUser(db);
        var model = "vip-test-" + Guid.NewGuid().ToString("N");
        var id = db.Insertable(new AiImagePointPriceEntity
        {
            ModelCode = model, ResolutionCode = "1k", QualityCode = "", Points = 32, Status = 1, CreatedAt = DateTime.Now
        }).ExecuteReturnBigIdentity();
        var user = Mock.Of<ICurrentUser>(x => x.UserId == userId);
        var models = new Mock<IAiImageModelConfigService>();
        models.Setup(x => x.ResolveAsync(model, null, It.IsAny<CancellationToken>())).ReturnsAsync(new ResolvedAiImageModelConfig { ModelCode = model });
        var filter = new Mock<IAiPromptFilter>();
        filter.Setup(x => x.EnsureAllowedAsync(It.IsAny<string>(), "prompt", It.IsAny<CancellationToken>())).ReturnsAsync(new AiPromptFilterResult(true, 1, null));
        var queue = new AiImageTaskQueue();
        var real = new AiImageAdmissionService(redis, Options.Create(new RedisOptions { InstanceName = model }),
            Options.Create(new AiCostControlOptions()), queue, NullLogger<AiImageAdmissionService>.Instance);
        var gate = new Mock<IAiImageAdmissionService>(MockBehavior.Strict);
        var priceAtReservation = 40;
        RedisKey daily = default;
        gate.Setup(x => x.ReserveAsync(userId, It.IsAny<string>(), It.IsAny<string>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (long uid, string key, string fingerprint, int count, int points, CancellationToken ct) =>
            {
                var reservation = await real.ReserveAsync(uid, key, fingerprint, count, points, ct);
                daily = $"{model}:security:ai:daily:{reservation.QuotaDate}:points:{uid}";
                await writer.Updateable<AiImagePointPriceEntity>().SetColumns(x => x.Points == priceAtReservation).Where(x => x.Id == id).ExecuteCommandAsync();
                return reservation;
            });
        gate.Setup(x => x.CancelAsync(It.IsAny<AiImageAdmissionReservation>())).Returns((AiImageAdmissionReservation r) => real.CancelAsync(r));
        gate.Setup(x => x.AdjustPointCostAsync(It.IsAny<AiImageAdmissionReservation>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((AiImageAdmissionReservation r, int points, CancellationToken ct) => real.AdjustPointCostAsync(r, points, ct));
        gate.Setup(x => x.BindTaskAsync(It.IsAny<AiImageAdmissionReservation>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((AiImageAdmissionReservation r, long task, CancellationToken ct) => real.BindTaskAsync(r, task, ct));
        var nano = new NanoBananaImageService(http, models.Object, new PointService(db, user), db, user, queue, gate.Object,
            Options.Create(new PromptLibraryOptions()), Mock.Of<IAiMediaPathResolver>(), filter.Object, Mock.Of<IMediaAssetService>(),
            Options.Create(new AiImageSizeModeOptions()), NullLogger<NanoBananaImageService>.Instance);
        CreateNanoBananaImageTaskRequest Request(int ceiling) => new()
        {
            ModelCode = model, Prompt = "test", ResolutionCode = "1k", AspectRatioCode = "1:1",
            IdempotencyKey = "race-price-and-reserve", ExpectedUnitPoints = ceiling
        };

        var error = await Assert.ThrowsAsync<AppException>(() => nano.CreateAsync(Request(32), default));
        Assert.Equal(MachineErrorCodes.PriceChanged, error.MachineCode);
        Assert.Equal(0, (long)await redis.GetDatabase().StringGetAsync(daily));
        Assert.False(await db.Queryable<AiImageTaskEntity>().AnyAsync(x => x.UserId == userId));
        Assert.False(await db.Queryable<UserPointDetailEntity>().AnyAsync(x => x.UserId == userId));
        priceAtReservation = 35;
        var success = await nano.CreateAsync(Request(40), default);
        Assert.Equal(35, (long)await redis.GetDatabase().StringGetAsync(daily));
        Assert.Equal(35, (await db.Queryable<AiImageTaskEntity>().SingleAsync(x => x.Id == success)).PointCost);
        Assert.Equal(465, (await db.Queryable<SysUserEntity>().SingleAsync(x => x.Id == userId)).PointBalance);
        await writer.Updateable<AiImagePointPriceEntity>().SetColumns(x => x.Status == 0).Where(x => x.Id == id).ExecuteCommandAsync();
        var replay = await nano.CreateAsync(Request(1), default);
        Assert.Equal(success, replay);
        Assert.Equal(1, await db.Queryable<UserPointDetailEntity>().CountAsync(x => x.UserId == userId));
    }

    [PricingDatabaseFact]
    public async Task FourGenerationEndpoints_RejectNonVipBeforeQuotaOrBilling()
    {
        using var db = Open();
        using var http = new HttpClient();
        var userId = CreateUser(db);
        var model = "vip-test-" + Guid.NewGuid().ToString("N");
        db.Insertable(new AiImageModelPolicyEntity { ModelCode = model, AccessLevel = "vip_only", UpdatedAt = DateTime.UtcNow }).ExecuteCommand();
        // A stale privileged JWT must not grant access after the stored flag has been removed.
        var user = Mock.Of<ICurrentUser>(x => x.UserId == userId && x.IsSuperAdmin);
        var models = new Mock<IAiImageModelConfigService>();
        models.Setup(x => x.ResolveAsync(model, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedAiImageModelConfig { ModelCode = model, ProviderProtocol = "openai-image" });
        var admission = new Mock<IAiImageAdmissionService>(MockBehavior.Strict);
        var filter = new Mock<IAiPromptFilter>();
        filter.Setup(x => x.EnsureAllowedAsync(It.IsAny<string>(), "prompt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiPromptFilterResult(true, 1, null));
        var media = Mock.Of<IMediaAssetService>();
        var queue = Mock.Of<IAiImageTaskQueue>();
        var path = Mock.Of<IAiMediaPathResolver>();
        var catalog = Mock.Of<IAiImageCatalogService>();
        var rollout = Mock.Of<IAiSizeModeRolloutPolicy>();
        var size = Options.Create(new AiImageSizeModeOptions());
        var prompts = Options.Create(new PromptLibraryOptions());
        var points = new PointService(db, user);
        var gpt = new AiImageService(http, models.Object, points, db, user, queue, admission.Object,
            Options.Create(new OpenAiOptions()), size, prompts, path, filter.Object, media, catalog, rollout, NullLogger<AiImageService>.Instance);
        var nano = new NanoBananaImageService(http, models.Object, points, db, user, queue, admission.Object,
            prompts, path, filter.Object, media, size, NullLogger<NanoBananaImageService>.Instance);
        var controller = new AiImagesController(gpt, nano, models.Object, catalog, rollout);
        const string key = "nonvip-denied-0001";
#pragma warning disable CS0618 // The legacy mobile endpoint must enforce the same access policy.
        Func<Task>[] endpoints = [
            () => controller.Create(new CreateAiImageTaskRequest { ModelCode = model, Prompt = "test", IdempotencyKey = key }, default),
            () => controller.Generate(new GenerateAiImageRequest { ModelCode = model, Prompt = "test", IdempotencyKey = key }, default),
            () => controller.CreateNanoBananaImage(new CreateNanoBananaImageTaskRequest { ModelCode = model, Prompt = "test", IdempotencyKey = key, ResolutionCode = "1k", AspectRatioCode = "1:1" }, default),
            () => controller.GenerateNanoBananaImage(new GenerateNanoBananaImageRequest { ModelCode = model, Prompt = "test", IdempotencyKey = key, ResolutionCode = "1k", AspectRatioCode = "1:1" }, default)
        ];
#pragma warning restore CS0618
        foreach (var call in endpoints)
        {
            var error = await Assert.ThrowsAsync<AppException>(call);
            Assert.Equal(403, error.Code);
            Assert.Equal(MachineErrorCodes.VipRequired, error.MachineCode);
        }
        admission.VerifyNoOtherCalls();
        Assert.False(await db.Queryable<AiImageTaskEntity>().AnyAsync(x => x.UserId == userId));
        Assert.False(await db.Queryable<UserPointDetailEntity>().AnyAsync(x => x.UserId == userId));
    }
}
