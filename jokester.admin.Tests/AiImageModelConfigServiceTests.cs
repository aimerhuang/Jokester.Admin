using jokester.admin.Application.Services;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiImageModelConfigServiceTests
{
    [Fact]
    public async Task ResolveRoutesAsync_ReturnsPrimaryThenFallback_WithIndependentProviderModels()
    {
        using var context = new TestContext();
        context.Seed(
            CreateConfig(1, AiImageModelConfigService.PrimaryRouteRole, "gpt-image-2", "https://primary.example/v1"),
            CreateConfig(2, AiImageModelConfigService.FallbackRouteRole, "gpt-image-2-1k", "https://fallback.example/v1"));

        var routes = await context.Service.ResolveRoutesAsync("gpt-image-2", "1k", default);

        Assert.Collection(
            routes,
            primary =>
            {
                Assert.Equal(AiImageModelConfigService.PrimaryRouteRole, primary.RouteRole);
                Assert.Equal("gpt-image-2", primary.ProviderModel);
                Assert.Equal("https://primary.example/v1", primary.BaseUrl);
            },
            fallback =>
            {
                Assert.Equal(AiImageModelConfigService.FallbackRouteRole, fallback.RouteRole);
                Assert.Equal("gpt-image-2-1k", fallback.ProviderModel);
                Assert.Equal("https://fallback.example/v1", fallback.BaseUrl);
            });
    }

    [Fact]
    public async Task ResolveRoutesAsync_UsesFallback_WhenPrimaryIsDisabled()
    {
        using var context = new TestContext();
        context.Seed(
            CreateConfig(1, AiImageModelConfigService.PrimaryRouteRole, "gpt-image-2", "https://primary.example/v1", status: 0),
            CreateConfig(2, AiImageModelConfigService.FallbackRouteRole, "gpt-image-2-1k", "https://fallback.example/v1"));

        var routes = await context.Service.ResolveRoutesAsync("gpt-image-2", "1k", default);
        var selected = await context.Service.ResolveAsync("gpt-image-2", "1k", default);

        var fallback = Assert.Single(routes);
        Assert.Equal(AiImageModelConfigService.FallbackRouteRole, fallback.RouteRole);
        Assert.Equal(fallback.Id, selected.Id);
    }

    [Fact]
    public async Task ResolveRoutesAsync_UsesGenericFallback_ForRequestedResolution()
    {
        using var context = new TestContext();
        context.Seed(
            CreateConfig(1, AiImageModelConfigService.PrimaryRouteRole, "gpt-image-2", "https://primary.example/v1", resolutionCode: "4k"),
            CreateConfig(2, AiImageModelConfigService.FallbackRouteRole, "gpt-image-2-backup", "https://fallback.example/v1", resolutionCode: string.Empty));

        var routes = await context.Service.ResolveRoutesAsync("gpt-image-2", "4k", default);

        Assert.Equal(2, routes.Count);
        Assert.Equal("4k", routes[0].ResolutionCode);
        Assert.Null(routes[1].ResolutionCode);
    }

    [Fact]
    public async Task GetEnabledModelsAsync_ReturnsOnlyPricedRoutedResolutions()
    {
        using var context = new TestContext();
        context.Seed(
            CreateConfig(1, AiImageModelConfigService.PrimaryRouteRole, "gpt-image-2-1k", "https://primary.example/v1", resolutionCode: "1k"),
            CreateConfig(2, AiImageModelConfigService.FallbackRouteRole, "gpt-image-2-4k", "https://fallback.example/v1", resolutionCode: "4k"));
        context.SeedParameters(
            CreateParameter(1, "resolution", "1k"),
            CreateParameter(2, "resolution", "2k"),
            CreateParameter(3, "resolution", "4k"),
            CreateParameter(4, "quality", "med"),
            CreateParameter(5, "aspect_ratio", "1:1"));
        context.SeedPrices(
            CreatePrice(1, "1k"),
            CreatePrice(2, "2k"),
            CreatePrice(3, "4k"));

        var model = Assert.Single(await context.Service.GetEnabledModelsAsync(default));

        Assert.Equal(["1k", "4k"], model.Resolutions);
        Assert.Equal(["med"], model.Qualities);
        Assert.Equal(["1:1"], model.AspectRatios);
    }

    [Fact]
    public async Task GetEnabledModelsAsync_Includes2K_WhenGenericRouteAndPriceAreEnabled()
    {
        using var context = new TestContext();
        context.Seed(
            CreateConfig(
                1,
                AiImageModelConfigService.PrimaryRouteRole,
                "gpt-image-2",
                "https://api.openai.com/v1",
                resolutionCode: string.Empty));
        context.SeedParameters(
            CreateParameter(1, "resolution", "1k"),
            CreateParameter(2, "resolution", "2k"),
            CreateParameter(3, "resolution", "4k"),
            CreateParameter(4, "quality", "med"),
            CreateParameter(5, "aspect_ratio", "16:9"));
        context.SeedPrices(
            CreatePrice(1, "1k"),
            CreatePrice(2, "2k"),
            CreatePrice(3, "4k"));

        var model = Assert.Single(await context.Service.GetEnabledModelsAsync(default));

        Assert.Equal(["1k", "2k", "4k"], model.Resolutions);
    }

    [Fact]
    public async Task GetEnabledModelsAsync_Excludes2K_WhenItsParameterIsDisabled()
    {
        using var context = new TestContext();
        context.Seed(
            CreateConfig(
                1,
                AiImageModelConfigService.PrimaryRouteRole,
                "gpt-image-2",
                "https://api.openai.com/v1",
                resolutionCode: "2k"));
        context.SeedParameters(
            CreateParameter(1, "resolution", "1k"),
            CreateParameter(2, "quality", "med"),
            CreateParameter(3, "aspect_ratio", "16:9"));
        context.SeedPrices(CreatePrice(1, "2k"));

        var model = Assert.Single(await context.Service.GetEnabledModelsAsync(default));

        Assert.Empty(model.Resolutions);
    }

    [Fact]
    public async Task GetEnabledModelsAsync_ExcludesAutoForGptAndKeepsItForGemini()
    {
        using var context = new TestContext();
        context.Seed(
            CreateConfig(1, AiImageModelConfigService.PrimaryRouteRole, "gpt-image-2", "https://gpt.example/v1"),
            CreateConfig(
                2,
                AiImageModelConfigService.PrimaryRouteRole,
                "nano-banana-2",
                "https://gemini.example/v1",
                modelCode: AiImageModelConfigService.DefaultNanoBananaModelCode,
                provider: AiImageModelConfigService.GeminiImageProtocol));
        context.SeedParameters(
            CreateParameter(1, "resolution", "1k"),
            CreateParameter(2, "quality", "med"),
            CreateParameter(3, "aspect_ratio", "auto"),
            CreateParameter(4, "aspect_ratio", "1:1"));
        context.SeedPrices(
            CreatePrice(1, "1k"),
            CreatePrice(2, "1k", AiImageModelConfigService.DefaultNanoBananaModelCode));

        var models = (await context.Service.GetEnabledModelsAsync(default))
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(["1:1"], models[AiImageModelConfigService.DefaultGptModelCode].AspectRatios);
        Assert.Equal(["auto", "1:1"], models[AiImageModelConfigService.DefaultNanoBananaModelCode].AspectRatios);
    }

    [Theory]
    [InlineData("gpt-image-2.5-flare", "GPT Image 2.5 Flare")]
    [InlineData("gpt-image-2.5-sunburst", "GPT Image 2.5 Sunburst")]
    [InlineData("gpt-image-2.6-flare", "GPT Image 2.6 Flare")]
    [InlineData("gpt-image-2.6-sunburst", "GPT Image 2.6 Sunburst")]
    public async Task GetEnabledModelsAsync_ExposesVersionedGptModelAsDistinctModel_WithItsOwnRoutesAndPrices(
        string modelCode,
        string modelName)
    {
        using var context = new TestContext();
        var primary = CreateConfig(
            2, AiImageModelConfigService.PrimaryRouteRole, modelCode,
            "https://primary.example/v1", resolutionCode: string.Empty, modelCode: modelCode);
        primary.ModelName = modelName;
        var fallback = CreateConfig(
            3, AiImageModelConfigService.FallbackRouteRole, modelCode,
            "https://fallback.example/v1", resolutionCode: string.Empty, modelCode: modelCode);
        fallback.ModelName = modelName;
        context.Seed(
            CreateConfig(1, AiImageModelConfigService.PrimaryRouteRole, "gpt-image-2", "https://primary.example/v1"),
            primary,
            fallback);
        context.SeedParameters(
            CreateParameter(1, "resolution", "1k"),
            CreateParameter(2, "resolution", "2k"),
            CreateParameter(3, "resolution", "4k"),
            CreateParameter(4, "quality", "med"),
            CreateParameter(5, "aspect_ratio", "auto"),
            CreateParameter(6, "aspect_ratio", "1:1"));
        context.SeedPrices(
            CreatePrice(1, "1k"),
            CreatePrice(2, "1k", modelCode),
            CreatePrice(3, "2k", modelCode),
            CreatePrice(4, "4k", modelCode));

        var models = await context.Service.GetEnabledModelsAsync(default);
        var model = Assert.Single(models, option => option.Code == modelCode);
        Assert.Equal(2, models.Count);
        Assert.Equal(modelName, model.Name);
        Assert.Equal("openai", model.ProviderCode);
        Assert.Equal(["1k", "2k", "4k"], model.Resolutions);
        Assert.Equal(["med"], model.Qualities);
        Assert.Equal(["1:1"], model.AspectRatios);
        Assert.True(model.Capabilities.SupportsQuality);
        Assert.True(model.Capabilities.SupportsReferenceImages);

        foreach (var resolution in model.Resolutions)
        {
            var routes = await context.Service.ResolveRoutesAsync(modelCode, resolution, default);
            Assert.Equal(2, routes.Count);
            Assert.All(routes, route =>
            {
                Assert.Equal(modelCode, route.ModelCode);
                Assert.Equal(modelCode, route.ProviderModel);
                Assert.Equal(AiImageModelConfigService.OpenAiImageProtocol, route.ProviderProtocol);
            });
            Assert.Equal("primary-key", routes[0].ApiKey);
            Assert.Equal("fallback-key", routes[1].ApiKey);
        }
    }

    [Fact]
    public async Task GifCapabilitiesAndWorkerRoutesFollowConfiguredNativeRoutes()
    {
        using var context = new TestContext();
        const string flare = "gpt-image-2.6-flare";
        const string sunburst = "gpt-image-2.6-sunburst";
        var primary = CreateConfig(1, "primary", "regular-primary", "https://primary.example", resolutionCode: "4k", modelCode: flare);
        var fallback = CreateConfig(2, "fallback", "regular-fallback", "https://fallback.example", modelCode: flare);
        var unconfigured = CreateConfig(3, "primary", "regular", "https://primary.example", modelCode: sunburst);
        var configuredOther = CreateConfig(4, "primary", "regular", "https://primary.example", modelCode: "gpt-image-2");
        var disabled = CreateConfig(5, "primary", "regular", "https://primary.example", status: 0, modelCode: "gpt-image-2.5-flare");
        var gemini = CreateConfig(6, "primary", "nano", "https://primary.example", modelCode: "nano-banana-2",
            provider: AiImageModelConfigService.GeminiImageProtocol);
        primary.GifProviderModel = "native-primary";
        fallback.GifProviderModel = "native-backup";
        configuredOther.GifProviderModel = "native-other";
        disabled.GifProviderModel = "native-disabled";
        gemini.GifProviderModel = "native-gemini";
        context.Seed(primary, fallback, unconfigured, configuredOther, disabled, gemini);

        // Any OpenAI Images model is GIF-capable once a super admin gives an enabled route a native GIF model.
        var models = await context.Service.GetEnabledModelsAsync(default);
        Assert.Equal(9, models.Single(m => m.Code == flare).Capabilities.Gif!.DefaultFrameCount);
        Assert.NotNull(models.Single(m => m.Code == "gpt-image-2").Capabilities.Gif);
        Assert.All(models.Where(m => m.Code is not (flare or "gpt-image-2")), m => Assert.Null(m.Capabilities.Gif));
        var routes = await context.Service.ResolveGifRoutesAsync(flare, default);
        Assert.Equal(["native-primary", "native-backup"], routes.Select(r => r.ProviderModel));
        Assert.Equal("regular-primary", (await context.Service.ResolveAsync(flare, "4k", default)).ProviderModel);
        Assert.Equal(["native-other"], (await context.Service.ResolveGifRoutesAsync("gpt-image-2", default)).Select(r => r.ProviderModel));
        await Assert.ThrowsAsync<AppException>(() => context.Service.ResolveGifRoutesAsync(sunburst, default));
        await Assert.ThrowsAsync<AppException>(() => context.Service.ResolveGifRoutesAsync("gpt-image-2.5-flare", default));
        await Assert.ThrowsAsync<AppException>(() => context.Service.ResolveGifRoutesAsync("nano-banana-2", default));

        var draining = new AiImageModelConfigService(context.Db, Microsoft.Extensions.Options.Options.Create(
            new jokester.admin.Infrastructure.AiImageGifOptions { AcceptNewTasks = false }));
        Assert.All(await draining.GetEnabledModelsAsync(default), m => Assert.Null(m.Capabilities.Gif));
        Assert.Equal(2, (await draining.ResolveGifRoutesAsync(flare, default)).Count);
    }

    private static AiImageModelConfigEntity CreateConfig(
        long id,
        string routeRole,
        string providerModel,
        string baseUrl,
        int status = 1,
        string resolutionCode = "1k",
        string modelCode = AiImageModelConfigService.DefaultGptModelCode,
        string provider = AiImageModelConfigService.OpenAiImageProtocol)
    {
        return new AiImageModelConfigEntity
        {
            Id = id,
            ModelCode = modelCode,
            ModelName = "GPT Image 2",
            Provider = provider,
            ProviderModel = providerModel,
            ResolutionCode = resolutionCode,
            RouteRole = routeRole,
            BaseUrl = baseUrl,
            ApiKey = $"{routeRole}-key",
            TextToImagePath = "/images/generations",
            ImageToImagePath = "/images/edits",
            Sort = 1,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static AiImageParameterEntity CreateParameter(long id, string type, string code) => new()
    {
        Id = id,
        ParamType = type,
        ParamCode = code,
        ParamName = code,
        Sort = (int)id,
        Status = 1,
        CreatedAt = DateTime.UtcNow
    };

    private static AiImagePointPriceEntity CreatePrice(
        long id,
        string resolutionCode,
        string modelCode = AiImageModelConfigService.DefaultGptModelCode) => new()
    {
        Id = id,
        ModelCode = modelCode,
        ResolutionCode = resolutionCode,
        QualityCode = "med",
        Points = 10,
        PriceAmount = 0.1m,
        Currency = "CNY",
        Sort = (int)id,
        Status = 1,
        CreatedAt = DateTime.UtcNow
    };

    private sealed class TestContext : IDisposable
    {
        public TestContext()
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
                CREATE TABLE ai_image_model_config (
                    gif_provider_model TEXT NULL,
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    model_code TEXT NOT NULL,
                    model_name TEXT NOT NULL,
                    provider TEXT NOT NULL,
                    provider_model TEXT NOT NULL,
                    resolution_code TEXT NOT NULL DEFAULT '',
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
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );
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
            Service = new AiImageModelConfigService(Db);
        }

        public SqlSugarClient Db { get; }

        public AiImageModelConfigService Service { get; }

        public void Seed(params AiImageModelConfigEntity[] configs)
        {
            Db.Insertable(configs).ExecuteCommand();
        }

        public void SeedParameters(params AiImageParameterEntity[] parameters) =>
            Db.Insertable(parameters).ExecuteCommand();

        public void SeedPrices(params AiImagePointPriceEntity[] prices) =>
            Db.Insertable(prices).ExecuteCommand();

        public void Dispose()
        {
            Db.Dispose();
        }
    }
}
