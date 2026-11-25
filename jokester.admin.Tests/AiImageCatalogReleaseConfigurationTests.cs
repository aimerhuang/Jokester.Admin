using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiImageCatalogReleaseConfigurationTests
{
    [Fact]
    public async Task Configuration_PublishesIdempotentExplicitAndAutoCatalog()
    {
        using var context = new CatalogContext();
        context.SeedLegacyCatalog();
        var options = ApprovedOptions();
        var now = new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc);

        var first = await AiImageCatalogReleaseConfiguration.RunAsync(context.Db, options, now);
        var second = await AiImageCatalogReleaseConfiguration.RunAsync(context.Db, options, now.AddMinutes(1));

        Assert.False(first.ReusedExistingRelease);
        Assert.True(second.ReusedExistingRelease);
        Assert.Equal(first.ModelReleaseId, second.ModelReleaseId);
        Assert.Equal(1, context.Db.Queryable<AiImageModelReleaseEntity>().Count());
        Assert.Equal(first.ModelReleaseId,
            context.Db.Queryable<AiImageCurrentReleaseEntity>().Single().ModelReleaseId);
        Assert.Equal(["1k", "2k", "4k"], context.Db.Queryable<AiImageParameterEntity>()
            .Where(x => x.ParamType == "resolution" && x.Status == 1 && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .Select(x => x.ParamCode)
            .ToList());
        Assert.Equal(4, first.ExplicitRouteCount);
        Assert.Equal(1, first.AutoRouteCount);
        Assert.Equal(9, first.ExplicitPriceCount);
        Assert.Equal(3, first.AutoPriceCount);

        var twoK = context.Db.Queryable<AiImagePointPriceEntity>().Where(x => x.ResolutionCode == "2k").OrderBy(x => x.Sort).ToList();
        Assert.Equal(new int?[] { 12, 24, 48 }, twoK.Select(x => x.VipPoints));
        Assert.All(twoK, row => Assert.Equal(1, row.Revision));
        var audits = context.Db.Queryable<AiImagePricingAuditEntity>().ToList();
        Assert.Equal(3, audits.Count);
        Assert.Single(audits.Select(x => x.BatchId).Distinct());
        Assert.All(audits, audit => { Assert.Equal(0, audit.OperatorUserId); Assert.Equal("create", audit.Action); });
        Assert.Equal(2, context.Db.Queryable<AiImagePricingSettingEntity>().Single().PricingRevision);

        var autoRoute = context.Db.Queryable<AiImageModelReleaseRouteEntity>()
            .Single(x => x.SizeMode == "auto");
        Assert.Empty(autoRoute.ResolutionCode);
        Assert.True(autoRoute.VerifiedGenerations);
        Assert.True(autoRoute.VerifiedEdits);
        Assert.True(autoRoute.VerifiedMaskEdits);
        Assert.Equal(64, autoRoute.SecretVersionHash.Length);

        var autoPrices = context.Db.Queryable<AiImageModelReleasePriceEntity>()
            .Where(x => x.PricingMode == "auto")
            .OrderBy(x => x.QualityCode)
            .ToList();
        Assert.All(autoPrices, price => Assert.Empty(price.ResolutionCode));
        Assert.Equal(["high", "low", "med"], autoPrices.Select(x => x.QualityCode));
    }

    [Fact]
    public async Task Configuration_PreservesDisabledAndDeletedTwoKPricesAndFillsOnlyMissingSlot()
    {
        using var context = new CatalogContext();
        context.SeedLegacyCatalog();
        var db = context.Db;
        db.Insertable(new AiImageModelPolicyEntity { ModelCode = "gpt-image-2", VipDiscountPercent = 50 }).ExecuteCommand();
        foreach (var quality in new[] { "high", "med" })
        {
            db.Insertable(new AiImagePointPriceEntity
            {
                ModelCode = "gpt-image-2", ResolutionCode = "2k", QualityCode = quality, Points = 77, VipPoints = 31,
                VipPriceMode = "manual", Revision = 7, Status = 0, IsDeleted = quality == "high", PriceAmount = 1.23m,
                Sort = 42, UpdatedBy = 19, CreatedAt = DateTime.UtcNow
            }).ExecuteCommand();
        }
        await AiImageCatalogReleaseConfiguration.RunAsync(db, ApprovedOptions());
        await AiImageCatalogReleaseConfiguration.RunAsync(db, ApprovedOptions());
        var rows = db.Queryable<AiImagePointPriceEntity>().Where(x => x.ResolutionCode == "2k").ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows.Where(x => x.QualityCode != "low"), row =>
        {
            Assert.Equal((77, (int?)31, "manual", 7, 0, 1.23m, 42, (long?)19),
                (row.Points, row.VipPoints, row.VipPriceMode, row.Revision, row.Status, row.PriceAmount, row.Sort, row.UpdatedBy));
            Assert.Equal(row.QualityCode == "high", row.IsDeleted);
            Assert.Null(row.UpdatedAt);
        });
        var created = rows.Single(x => x.QualityCode == "low");
        Assert.Equal((15, (int?)8, "follow", 1), (created.Points, created.VipPoints, created.VipPriceMode, created.Revision));
        var audit = db.Queryable<AiImagePricingAuditEntity>().Single();
        Assert.Equal(created.Id.ToString(), audit.TargetId);
        Assert.Null(audit.BeforeJson);
        Assert.Contains("\"vipPoints\":8", audit.AfterJson, StringComparison.Ordinal);
        Assert.Equal(2, db.Queryable<AiImagePricingSettingEntity>().Single().PricingRevision);
    }

    [Fact]
    public async Task Configuration_RollsBackTwoKPricesAuditAndRevisionOnPublishFailure()
    {
        using var context = new CatalogContext();
        context.SeedLegacyCatalog();
        var db = context.Db;
        db.Ado.ExecuteCommand("UPDATE ai_image_model_config SET status=0 WHERE resolution_code='4k'");
        await Assert.ThrowsAsync<InvalidOperationException>(() => AiImageCatalogReleaseConfiguration.RunAsync(db, ApprovedOptions()));
        Assert.Equal(0, db.Queryable<AiImagePointPriceEntity>().Count(x => x.ResolutionCode == "2k"));
        Assert.Equal(0, db.Queryable<AiImagePricingAuditEntity>().Count());
        Assert.Equal(1, db.Queryable<AiImagePricingSettingEntity>().Single().PricingRevision);
    }

    [Fact]
    public async Task Configuration_RejectsChangingAnImmutableCatalogVersion()
    {
        using var context = new CatalogContext();
        context.SeedLegacyCatalog();
        var options = ApprovedOptions();
        await AiImageCatalogReleaseConfiguration.RunAsync(context.Db, options);
        var changed = options with
        {
            AutoPoints = new Dictionary<string, int>(options.AutoPoints, StringComparer.OrdinalIgnoreCase)
            {
                ["med"] = 61
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AiImageCatalogReleaseConfiguration.RunAsync(context.Db, changed));

        Assert.Contains("new CatalogVersion", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, context.Db.Queryable<AiImageModelReleaseEntity>().Count());
        Assert.Equal(3, context.Db.Queryable<AiImageModelReleasePriceEntity>()
            .Where(x => x.PricingMode == "auto")
            .Count());
    }

    [Fact]
    public async Task Configuration_RequiresAllAutoOperationEvidenceBeforeWriting()
    {
        using var context = new CatalogContext();
        context.SeedLegacyCatalog();
        var options = ApprovedOptions() with { AutoVerifiedMaskEdits = false };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AiImageCatalogReleaseConfiguration.RunAsync(context.Db, options));

        Assert.Contains("mask-edits", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, context.Db.Queryable<AiImageModelReleaseEntity>().Count());
    }

    private static AiImageCatalogReleaseConfigurationOptions ApprovedOptions() => new()
    {
        Approved = true,
        ModelCode = "gpt-image-2",
        ModelName = "GPT Image 2",
        CatalogVersion = "imgcat_test_20260823_01",
        ConsentProviderCode = "openai",
        ExplicitResolutionCodes = ["1k", "2k", "4k"],
        EnsureGptImage2TwoK = true,
        PublishAuto = true,
        AutoRouteSourceResolutionCode = "1k",
        AutoVerifiedGenerations = true,
        AutoVerifiedEdits = true,
        AutoVerifiedMaskEdits = true,
        AutoPoints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["low"] = 50,
            ["med"] = 60,
            ["high"] = 80
        },
        AutoPriceAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["low"] = 0.50m,
            ["med"] = 0.60m,
            ["high"] = 0.80m
        },
        Currency = "CNY"
    };

    private sealed class CatalogContext : IDisposable
    {
        public CatalogContext()
        {
            SQLitePCL.Batteries_V2.Init();
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=:memory:",
                DbType = SqlSugar.DbType.Sqlite,
                IsAutoCloseConnection = false
            });
            Db.Ado.ExecuteCommand("""
                CREATE TABLE ai_image_pricing_setting (id INTEGER PRIMARY KEY, default_vip_discount_percent INTEGER,
                    revision INTEGER, pricing_revision INTEGER, updated_by INTEGER, updated_at TEXT);
                INSERT INTO ai_image_pricing_setting (id, default_vip_discount_percent, revision, pricing_revision) VALUES (1,80,0,1);
                CREATE TABLE ai_image_model_policy (model_code TEXT PRIMARY KEY, access_level TEXT, required_tier_code TEXT,
                    vip_discount_percent INTEGER, revision INTEGER, updated_by INTEGER, updated_at TEXT);
                CREATE TABLE ai_image_pricing_audit (id INTEGER PRIMARY KEY AUTOINCREMENT, batch_id TEXT, target_type TEXT,
                    target_id TEXT, model_code TEXT, resolution_code TEXT, quality_code TEXT, action TEXT, before_json TEXT,
                    after_json TEXT, reason TEXT, operator_user_id INTEGER, operator_ip TEXT, created_at TEXT);
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
                    is_deleted INTEGER NOT NULL
                );
                CREATE UNIQUE INDEX uk_parameter ON ai_image_parameter(param_type, param_code);
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
                    is_deleted INTEGER NOT NULL
                );
                CREATE UNIQUE INDEX uk_legacy_price ON ai_image_point_price(model_code, resolution_code, quality_code);
                CREATE TABLE ai_image_model_config (
                    gif_provider_model TEXT NULL,
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    model_code TEXT NOT NULL,
                    model_name TEXT NOT NULL,
                    provider TEXT NOT NULL,
                    provider_model TEXT NOT NULL,
                    resolution_code TEXT NOT NULL,
                    route_role TEXT NOT NULL,
                    base_url TEXT NOT NULL,
                    api_key TEXT NOT NULL,
                    text_to_image_path TEXT NOT NULL,
                    image_to_image_path TEXT NOT NULL,
                    request_format TEXT NOT NULL DEFAULT 'openai-size',
                    sort INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    revision INTEGER NOT NULL DEFAULT 0,
                    api_key_updated_at TEXT NULL,
                    updated_by INTEGER NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL,
                    is_deleted INTEGER NOT NULL
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
                CREATE UNIQUE INDEX uk_release ON ai_image_model_release(model_code, catalog_version);
                CREATE TABLE ai_image_model_current_release (
                    model_code TEXT PRIMARY KEY,
                    model_release_id INTEGER NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE ai_image_model_release_route (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    model_release_id INTEGER NOT NULL,
                    route_config_id INTEGER NOT NULL,
                    size_mode TEXT NOT NULL,
                    resolution_code TEXT NOT NULL,
                    route_role TEXT NOT NULL,
                    provider_protocol TEXT NOT NULL,
                    consent_provider_code TEXT NOT NULL,
                    provider_model TEXT NOT NULL,
                    base_url TEXT NOT NULL,
                    text_to_image_path TEXT NOT NULL,
                    image_to_image_path TEXT NOT NULL,
                    secret_version_hash TEXT NOT NULL,
                    verified_generations INTEGER NOT NULL,
                    verified_edits INTEGER NOT NULL,
                    verified_mask_edits INTEGER NOT NULL,
                    sort INTEGER NOT NULL
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
                    sort INTEGER NOT NULL,
                    status INTEGER NOT NULL
                );
                """);
        }

        public SqlSugarClient Db { get; }

        public void SeedLegacyCatalog()
        {
            var now = new DateTime(2026, 8, 23, 7, 0, 0, DateTimeKind.Utc);
            Db.Insertable(new[]
            {
                Resolution("1k", 1024, 1, now),
                Resolution("4k", 3840, 3, now)
            }).ExecuteCommand();
            Db.Insertable(new[]
            {
                Route("1k", "primary", 1, now),
                Route("1k", "fallback", 11, now, "gpt-image-2-1k"),
                Route("4k", "primary", 3, now)
            }).ExecuteCommand();
            var prices = new List<AiImagePointPriceEntity>();
            prices.AddRange(Prices("1k", [10, 20, 30], 1, now));
            prices.AddRange(Prices("4k", [50, 60, 80], 7, now));
            Db.Insertable(prices).ExecuteCommand();
        }

        public void Dispose() => Db.Dispose();

        private static AiImageParameterEntity Resolution(string code, int longSide, int sort, DateTime now) => new()
        {
            ParamType = "resolution",
            ParamCode = code,
            ParamName = code.ToUpperInvariant(),
            ValueInt1 = longSide,
            Sort = sort,
            Status = 1,
            CreatedAt = now
        };

        private static AiImageModelConfigEntity Route(
            string resolution,
            string role,
            int sort,
            DateTime now,
            string providerModel = "gpt-image-2") => new()
        {
            ModelCode = "gpt-image-2",
            ModelName = $"GPT Image 2 {resolution.ToUpperInvariant()}",
            Provider = "openai-image",
            ProviderModel = providerModel,
            ResolutionCode = resolution,
            RouteRole = role,
            BaseUrl = "https://images.example.test/v1",
            ApiKey = $"test-secret-{resolution}-{role}",
            TextToImagePath = "/images/generations",
            ImageToImagePath = "/images/edits",
            Sort = sort,
            Status = 1,
            CreatedAt = now
        };

        private static IEnumerable<AiImagePointPriceEntity> Prices(
            string resolution,
            int[] points,
            int firstSort,
            DateTime now)
        {
            var qualities = new[] { "low", "med", "high" };
            return qualities.Select((quality, index) => new AiImagePointPriceEntity
            {
                ModelCode = "gpt-image-2",
                ResolutionCode = resolution,
                QualityCode = quality,
                Points = points[index],
                PriceAmount = points[index] / 100m,
                Currency = "CNY",
                Sort = firstSort + index,
                Status = 1,
                CreatedAt = now
            });
        }
    }
}
