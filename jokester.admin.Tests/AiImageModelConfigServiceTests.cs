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

    private static AiImageModelConfigEntity CreateConfig(
        long id,
        string routeRole,
        string providerModel,
        string baseUrl,
        int status = 1,
        string resolutionCode = "1k")
    {
        return new AiImageModelConfigEntity
        {
            Id = id,
            ModelCode = "gpt-image-2",
            ModelName = "GPT Image 2",
            Provider = $"{routeRole}-provider",
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
                """);
            Service = new AiImageModelConfigService(Db);
        }

        public SqlSugarClient Db { get; }

        public AiImageModelConfigService Service { get; }

        public void Seed(params AiImageModelConfigEntity[] configs)
        {
            Db.Insertable(configs).ExecuteCommand();
        }

        public void Dispose()
        {
            Db.Dispose();
        }
    }
}
