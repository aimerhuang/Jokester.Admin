using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Services;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiImageParameterOptionsTests
{
    [Theory]
    [InlineData(false, 15, "standard")]
    [InlineData(true, 12, "vip")]
    public async Task GetParameterOptionsAsync_ReportsPersonalizedQuoteWithoutChangingListPrice(bool isVip, int effective, string tier)
    {
        SQLitePCL.Batteries_V2.Init();
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute
        });
        db.Ado.ExecuteCommand("""
            CREATE TABLE ai_image_pricing_setting (id INTEGER PRIMARY KEY, pricing_revision INTEGER, revision INTEGER, default_vip_discount_percent INTEGER, updated_by INTEGER, updated_at TEXT);
            INSERT INTO ai_image_pricing_setting (id, pricing_revision) VALUES (1, 19);
            CREATE TABLE ai_image_parameter (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                param_type TEXT NOT NULL,
                param_code TEXT NOT NULL,
                param_name TEXT NOT NULL,
                provider_value TEXT NULL,
                value_int_1 INTEGER NULL,
                value_int_2 INTEGER NULL,
                sort INTEGER NOT NULL,
                status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE ai_image_point_price (
                vip_points INTEGER NULL, vip_price_mode TEXT DEFAULT 'follow', revision INTEGER DEFAULT 0, updated_by INTEGER NULL,
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                model_code TEXT NOT NULL,
                resolution_code TEXT NOT NULL,
                quality_code TEXT NOT NULL,
                points INTEGER NOT NULL,
                price_amount NUMERIC NOT NULL,
                currency TEXT NOT NULL,
                sort INTEGER NOT NULL,
                status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0
            );
            """);
        db.Insertable(new AiImageParameterEntity
        {
            ParamType = "resolution",
            ParamCode = "1k",
            ParamName = "1K",
            ValueInt1 = 1024,
            Sort = 1,
            Status = 1,
            CreatedAt = DateTime.UtcNow
        }).ExecuteCommand();
        db.Insertable(new[]
        {
            new AiImageParameterEntity
            {
                ParamType = "aspect_ratio",
                ParamCode = "auto",
                ParamName = "Auto",
                Sort = 1,
                Status = 1,
                CreatedAt = DateTime.UtcNow
            },
            new AiImageParameterEntity
            {
                ParamType = "aspect_ratio",
                ParamCode = "1:1",
                ParamName = "1:1",
                ValueInt1 = 1,
                ValueInt2 = 1,
                Sort = 2,
                Status = 1,
                CreatedAt = DateTime.UtcNow
            }
        }).ExecuteCommand();
        db.Insertable(new AiImagePointPriceEntity
        {
            ModelCode = "gpt-image-2",
            ResolutionCode = "1k",
            QualityCode = "med",
            Points = 15,
            VipPoints = 12,
            PriceAmount = 1.235m,
            Currency = "CNY",
            Sort = 1,
            Status = 1,
            CreatedAt = DateTime.UtcNow
        }).ExecuteCommand();

        db.Ado.ExecuteCommand("""
            CREATE TABLE sys_user_membership_entitlement (id INTEGER PRIMARY KEY, user_id INTEGER, tier_code TEXT,
                source TEXT, business_key TEXT, status TEXT, starts_at TEXT, expires_at TEXT, revoked_at TEXT, created_at TEXT, updated_at TEXT)
            """);
        if (isVip)
            db.Insertable(new SysUserMembershipEntitlementEntity
            {
                UserId = 7, TierCode = "monthly_vip", Source = "recharge", BusinessKey = "test-redeem", Status = "active",
                StartsAt = DateTime.Now.AddDays(-1), ExpiresAt = DateTime.Now.AddDays(1), CreatedAt = DateTime.Now.AddDays(-1)
            }).ExecuteCommand();
        using var httpClient = new HttpClient();
        var service = new AiImageService(
            httpClient,
            Mock.Of<IAiImageModelConfigService>(),
            Mock.Of<IPointService>(),
            db,
            Mock.Of<ICurrentUser>(x => x.UserId == 7),
            Mock.Of<IAiImageTaskQueue>(),
            Mock.Of<IAiImageAdmissionService>(),
            Options.Create(new OpenAiOptions()),
            Options.Create(new AiImageSizeModeOptions()),
            Options.Create(new PromptLibraryOptions()),
            Mock.Of<IAiMediaPathResolver>(),
            Mock.Of<IAiPromptFilter>(),
            Mock.Of<IMediaAssetService>(),
            Mock.Of<IAiImageCatalogService>(),
            Mock.Of<IAiSizeModeRolloutPolicy>(),
            NullLogger<AiImageService>.Instance);

        var result = await service.GetParameterOptionsAsync(default);

        var price = Assert.Single(result.PointPrices);
        Assert.Equal(19, result.PricingRevision);
        Assert.Equal(15, price.Points);
        Assert.Equal(12, price.VipPoints);
        Assert.Equal(effective, price.EffectivePoints);
        Assert.Equal(tier, price.PricingTier);
        Assert.Equal(1.235m, price.PriceAmount);
        Assert.Equal(124, price.PriceMinorUnits);
        Assert.Equal(["1:1"], result.AspectRatios.Select(x => x.Code));
    }
}
