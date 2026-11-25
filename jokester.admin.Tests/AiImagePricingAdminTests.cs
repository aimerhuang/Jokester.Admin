using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.Services;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiImagePricingAdminTests : IDisposable
{
    private const string Gpt = "gpt-image-2";
    private readonly SqlSugarClient db;
    private readonly AiImagePricingAdminService admin;
    private readonly Mock<ICurrentUser> user = new();

    public AiImagePricingAdminTests()
    {
        SQLitePCL.Batteries_V2.Init();
        var nullability = new NullabilityInfoContext();
        db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:", DbType = DbType.Sqlite, IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (p, column) =>
                {
                    column.IsNullable = nullability.Create(p).WriteState == NullabilityState.Nullable;
                    if (column.IsIdentity) column.DataType = "INTEGER";
                }
            }
        });
        db.CodeFirst.InitTables(typeof(SysUserEntity), typeof(AiImagePointPriceEntity), typeof(AiImageModelPolicyEntity),
            typeof(AiImagePricingSettingEntity), typeof(AiImagePricingAuditEntity), typeof(AiImageModelConfigEntity),
            typeof(AiImageParameterEntity), typeof(AiImageCurrentReleaseEntity), typeof(AiImageModelReleaseEntity), typeof(AiImageModelReleasePriceEntity));
        db.Insertable(new AiImagePricingSettingEntity { Id = 1 }).ExecuteCommand();
        var id = db.Insertable(new SysUserEntity { UserName = "pricing-admin", IsSuperAdmin = true, Status = 1, CreatedAt = DateTime.Now }).ExecuteReturnBigIdentity();
        user.SetupGet(x => x.UserId).Returns(id);
        user.SetupGet(x => x.IsSuperAdmin).Returns(true);
        admin = new(db, user.Object, new HttpContextAccessor(), Options.Create(new AiImagePricingAdminOptions()));
        foreach (var (code, provider) in new[] { (Gpt, "openai-image"), ("nano-banana-2", "gemini-image") })
            db.Insertable(new AiImageModelConfigEntity { ModelCode = code, ModelName = code, Provider = provider, ResolutionCode = "", Status = 1, CreatedAt = DateTime.Now }).ExecuteCommand();
        foreach (var (type, code) in new[] { ("resolution", "1k"), ("quality", "low"), ("quality", "high") })
            db.Insertable(new AiImageParameterEntity { ParamType = type, ParamCode = code, CreatedAt = DateTime.Now }).ExecuteCommand();
    }

    private Task<PricingWriteResult> Create(string quality = "low", int points = 40, string model = Gpt) =>
        admin.CreateAsync(new(model, "1k", quality, points, 1, "测试新增"), default);
    private static EditPricingRow Edit(PricingRow row, int? points = null, string mode = "follow", int? vip = null, int? status = null) =>
        new() { Id = row.Id, Revision = row.Revision, Points = points ?? row.Points, VipPriceMode = mode, VipPoints = vip,
            PriceAmount = row.PriceAmount, Status = status ?? row.Status };
    private Task<DiscountPreview> Preview(string? model, int? pct) => admin.PreviewAsync(new(model, pct), default);
    private Task<PricingWriteResult> Discount(string? model, int? pct, DiscountPreview preview) =>
        admin.ApplyDiscountAsync(model, new(pct, preview.PreviewToken, "测试折扣"), default);
    private static async Task AssertError(string code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<AppException>(action)).MachineCode);

    [Fact]
    public async Task MatrixSerializesInheritedDiscountAndAbsentVipPriceAsNull()
    {
        await Create(points: 1);
        var matrix = await admin.GetAsync(default);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var model = JsonSerializer.SerializeToElement(matrix.Models.Single(m => m.ModelCode == Gpt), options);
        Assert.Equal(JsonValueKind.Null, model.GetProperty("vipDiscountPercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, model.GetProperty("prices")[0].GetProperty("vipPoints").ValueKind);
    }

    [Fact]
    public async Task MatrixIncludesMissingSlotsAndReleasePrices_CreateInitializesAndRejectsDuplicate()
    {
        var release = db.Insertable(new AiImageModelReleaseEntity { ModelCode = Gpt, CatalogVersion = "v1", Status = "published", SizeContractVersion = "size-mode-v1" }).ExecuteReturnBigIdentity();
        db.Insertable(new AiImageCurrentReleaseEntity { ModelCode = Gpt, ModelReleaseId = release }).ExecuteCommand();
        db.Insertable(new AiImageModelReleasePriceEntity { ModelCode = Gpt, ModelReleaseId = release, PricingMode = "auto", Points = 42 }).ExecuteCommand();
        var created = await Create(points: 3);
        Assert.Equal(2, created.PricingRevision);
        Assert.Null(Assert.Single(created.Prices).VipPoints);
        var overview = await admin.GetAsync(default);
        var model = overview.Models.Single(x => x.ModelCode == Gpt);
        Assert.Equal("high", Assert.Single(model.MissingPrices).QualityCode);
        Assert.Equal(42, Assert.Single(model.ReleasePrices).Points);
        var nano = overview.Models.Single(x => x.ModelCode == "nano-banana-2");
        Assert.Equal("", Assert.Single(nano.MissingPrices).QualityCode);
        Assert.Equal("legacy-aspect-auto", nano.SizeContractVersion);
        await AssertError("PRICE_ALREADY_EXISTS", () => Create());
    }

    [Fact]
    public async Task BatchValidatesWholeChangeAndRollsBackPriceVersionAndAudit()
    {
        var low = Assert.Single((await Create()).Prices);
        var high = Assert.Single((await Create("high")).Prices);
        await AssertError("VIP_PRICE_NOT_LOWER", () => admin.BatchAsync(new("测试", [Edit(low, 38), Edit(high, 30, "manual", 31)]), default));
        await AssertError("PRICE_CHANGE_TOO_LARGE", () => admin.BatchAsync(new("测试", [Edit(low, 2)]), default));
        await AssertError("PRICE_LAST_ENABLED", () => admin.BatchAsync(new("测试", [Edit(low, status: 0), Edit(high, status: 0)]), default));
        await AssertError("VALIDATION_FAILED", () => admin.BatchAsync(new(" ", [Edit(low, 35)]), default));
        await AssertError("VALIDATION_FAILED", () => admin.BatchAsync(new("测试", [Edit(low, 100001)]), default));
        Assert.Equal(3, (await admin.GetAsync(default)).PricingRevision);
        Assert.Equal(2, db.Queryable<AiImagePricingAuditEntity>().Count());
        Assert.Equal(40, db.Queryable<AiImagePointPriceEntity>().First(x => x.Id == low.Id).Points);
        var batch = await admin.BatchAsync(new("同批", [Edit(low, 1), Edit(high, 42, "manual", 32)], true), default);
        Assert.Null(batch.Prices[0].VipPoints);
        Assert.Equal(32, batch.Prices[1].VipPoints);
        Assert.Equal(4, batch.PricingRevision);
        await AssertError("PRICE_REVISION_CONFLICT", () => admin.BatchAsync(new("过时", [Edit(low, 43)]), default));
        var audit = await admin.AuditsAsync(new() { ChangeType = "points", OperatorUserId = user.Object.UserId }, default);
        Assert.Equal(4, audit.Total);
        Assert.Single(audit.Items.Where(x => x.Reason == "同批").Select(x => x.BatchId).Distinct());
    }

    [Fact]
    public async Task DiscountPreviewTracksAffectedRowsAndOnlyRecomputesAutomaticInheritedPrices()
    {
        var auto = Assert.Single((await Create()).Prices);
        var manual = Assert.Single((await Create("high")).Prices);
        await admin.BatchAsync(new("手动", [Edit(manual, mode: "manual", vip: 31)]), default);
        await Create("", 20, "nano-banana-2");
        await Discount("nano-banana-2", 50, await Preview("nano-banana-2", 50));
        var stale = await Preview(null, 70);
        Assert.Single(stale.Changes);
        Assert.Single(stale.ManualPrices);
        await admin.BatchAsync(new("改标准价", [Edit(auto, 42)]), default);
        await AssertError("PRICE_REVISION_CONFLICT", () => Discount(null, 70, stale));
        var preview = await Preview(null, 70);
        await admin.AccessAsync("nano-banana-2", new("vip_only", 1, "专属"), default);
        var applied = await Discount(null, 70, preview);
        Assert.Equal(30, Assert.Single(applied.Prices).VipPoints);
        Assert.Equal(1, (await admin.GetAsync(default)).Revision);
        var restored = await Discount("nano-banana-2", null, await Preview("nano-banana-2", null));
        Assert.Equal(14, Assert.Single(restored.Prices).VipPoints);
        Assert.Equal(31, db.Queryable<AiImagePointPriceEntity>().First(x => x.Id == manual.Id).VipPoints);
    }

    [Fact]
    public async Task AddedAutomaticRowInvalidatesPreviewAndHundredPercentClearsVipPrice()
    {
        await Create();
        var stale = await Preview(null, 100);
        await Create("high");
        await AssertError("PRICE_REVISION_CONFLICT", () => Discount(null, 100, stale));
        var fresh = await Preview(null, 100);
        var result = await Discount(null, 100, fresh);
        Assert.All(result.Prices, price => Assert.Null(price.VipPoints));
        Assert.Equal(2, result.Prices.Count);
    }

    [Fact]
    public async Task ConfirmedLastDisableIsAuditedAndUnrelatedPolicyDoesNotInvalidateDefaultPreview()
    {
        var row = Assert.Single((await Create()).Prices);
        var preview = await Preview(null, 60);
        await admin.AccessAsync("nano-banana-2", new("vip_only", 0, "访问"), default);
        await Discount(null, 60, preview);
        var updated = Assert.Single((await admin.GetAsync(default)).Models.Single(m => m.ModelCode == Gpt).Prices);
        await admin.BatchAsync(new("停用", [Edit(updated, status: 0)], ConfirmDisableLastPrice: true), default);
        Assert.Equal(0, db.Queryable<AiImagePointPriceEntity>().First(x => x.Id == row.Id).Status);
        Assert.Equal(1, (await admin.AuditsAsync(new() { ChangeType = "access" }, default)).Total);
        Assert.Equal(1, (await admin.AuditsAsync(new() { ChangeType = "discount" }, default)).Total);
    }

    [Fact]
    public async Task AccessAndPricingRequireActiveSuperAdminOnReadAndWrite()
    {
        await admin.AccessAsync(Gpt, new("vip_only", 0, "专属"), default);
        await AssertError("PRICE_REVISION_CONFLICT", () => admin.AccessAsync(Gpt, new("all", 0, "旧版"), default));
        db.Updateable<SysUserEntity>().SetColumns(x => x.IsSuperAdmin == false).Where(x => x.Id == 1).ExecuteCommand();
        await AssertError("RESOURCE_FORBIDDEN", () => admin.GetAsync(default));
        await AssertError("RESOURCE_FORBIDDEN", () => admin.AccessAsync(Gpt, new("all", 1, "取消"), default));
        user.SetupGet(x => x.IsSuperAdmin).Returns(false);
        await AssertError("RESOURCE_FORBIDDEN", () => admin.AuditsAsync(new(), default));
    }

    [Fact]
    public async Task FirstModelDiscountInsertsPolicyBeforeTouchingPriceRows()
    {
        await Create();
        var preview = await Preview(Gpt, 70);
        var statements = new List<string>();
        db.Aop.OnLogExecuting = (sql, _) => statements.Add(sql);
        try { await Discount(Gpt, 70, preview); }
        finally { db.Aop.OnLogExecuting = null; }
        var insertPolicy = statements.FindIndex(sql => sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase) && sql.Contains("ai_image_model_policy"));
        var firstPriceRead = statements.FindIndex(sql => sql.Contains("ai_image_point_price"));
        Assert.True(insertPolicy >= 0 && firstPriceRead > insertPolicy, string.Join("\n", statements));
        var policy = db.Queryable<AiImageModelPolicyEntity>().Single(x => x.ModelCode == Gpt);
        Assert.Equal((70, 1), (policy.VipDiscountPercent!.Value, policy.Revision));
    }

    [Fact]
    public async Task FailedFirstModelDiscountRollsBackInsertedPolicy()
    {
        await Create();
        var preview = await Preview(Gpt, 70);
        await Create("high");
        await AssertError("PRICE_REVISION_CONFLICT", () => Discount(Gpt, 70, preview));
        Assert.Equal(0, db.Queryable<AiImageModelPolicyEntity>().Count());
    }

    [Fact]
    public async Task CreatingSoftDeletedSlotRestoresRowWithAutomaticVipPriceAndAudit()
    {
        var original = Assert.Single((await Create(points: 40)).Prices);
        await admin.BatchAsync(new("手动", [Edit(original, mode: "manual", vip: 25)]), default);
        db.Updateable<AiImagePointPriceEntity>().SetColumns(x => x.IsDeleted == true).Where(x => x.Id == original.Id).ExecuteCommand();
        var model = (await admin.GetAsync(default)).Models.Single(x => x.ModelCode == Gpt);
        Assert.Contains(model.MissingPrices, slot => slot.QualityCode == "low");

        var restored = Assert.Single((await Create(points: 50)).Prices);
        Assert.Equal((original.Id, 50, 40, "follow", 3), (restored.Id, restored.Points, restored.VipPoints!.Value, restored.VipPriceMode, restored.Revision));
        Assert.False(db.Queryable<AiImagePointPriceEntity>().First(x => x.Id == original.Id).IsDeleted);
        var audit = db.Queryable<AiImagePricingAuditEntity>().OrderBy(x => x.Id, OrderByType.Desc).First();
        Assert.Equal("create", audit.Action);
        Assert.Contains("\"isDeleted\":true", audit.BeforeJson);
        await AssertError("PRICE_ALREADY_EXISTS", () => Create());
    }

    [Fact]
    public async Task ManualVipPriceMustBeSubmittedExplicitlyAndNullClearsDiscount()
    {
        var row = Assert.Single((await Create()).Prices);
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var omitted = JsonSerializer.Deserialize<EditPricingRow>(
            $$"""{"id":{{row.Id}},"revision":{{row.Revision}},"points":40,"vipPriceMode":"manual"}""", web)!;
        var cleared = JsonSerializer.Deserialize<EditPricingRow>(
            $$"""{"id":{{row.Id}},"revision":{{row.Revision}},"points":40,"vipPriceMode":"manual","vipPoints":null}""", web)!;
        Assert.False(omitted.HasVipPoints);
        Assert.True(cleared.HasVipPoints);
        Assert.False(JsonSerializer.SerializeToElement(cleared, web).TryGetProperty("hasVipPoints", out _));

        await AssertError("VALIDATION_FAILED", () => admin.BatchAsync(new("漏传", [omitted]), default));
        Assert.Equal(32, db.Queryable<AiImagePointPriceEntity>().First(x => x.Id == row.Id).VipPoints);
        var result = await admin.BatchAsync(new("无优惠", [cleared]), default);
        Assert.Null(Assert.Single(result.Prices).VipPoints);
        Assert.Equal("manual", result.Prices[0].VipPriceMode);
    }

    [Fact]
    public async Task MonitorReportsUnpricedRouteAndOnlyHumanAuditVolume()
    {
        await Create("low");
        await Create("high");
        var logs = new List<string>();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new Mock<ILogger<AiImagePricingMonitor>>();
        logger.Setup(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Callback(new InvocationAction(call =>
            {
                var text = call.Arguments[2].ToString()!;
                logs.Add(text);
                if (text.Contains("AI_PRICING_AUDIT_VOLUME")) observed.TrySetResult();
            }));
        using var services = new ServiceCollection().AddSingleton<ISqlSugarClient>(db).BuildServiceProvider();
        using var monitor = new AiImagePricingMonitor(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AiImagePricingAdminOptions { DailyAuditWarningThreshold = 1 }), logger.Object);
        await monitor.StartAsync(default);
        try { await observed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { await monitor.StopAsync(default); }
        Assert.Single(logs, message => message.Contains("AI_PRICING_ROUTE_UNPRICED") && message.Contains("nano-banana-2"));
        Assert.DoesNotContain(logs, message => message.Contains("AI_PRICING_ROUTE_UNPRICED") && message.Contains(Gpt));
    }

    public void Dispose() => db.Dispose();
}
