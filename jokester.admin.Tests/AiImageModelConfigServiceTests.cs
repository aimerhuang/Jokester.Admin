using jokester.admin.Application.Services;
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
                    sort INTEGER NOT NULL,
                    status INTEGER NOT NULL,
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
