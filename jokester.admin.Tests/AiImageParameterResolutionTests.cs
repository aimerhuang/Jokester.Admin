using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
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

public sealed class AiImageParameterResolutionTests
{
    private const int ProviderMaxEdge = 3840;
    private const long ProviderMinPixels = 655_360;
    private const long ProviderMaxPixels = 8_294_400;

    public static TheoryData<string, string, int, int> SupportedSizes => new()
    {
        { "1k", "1:1", 1024, 1024 },
        { "1k", "16:9", 1088, 608 },
        { "1k", "9:16", 608, 1088 },
        { "1k", "4:3", 1024, 768 },
        { "1k", "3:4", 768, 1024 },
        { "1k", "3:2", 1024, 688 },
        { "1k", "2:3", 688, 1024 },
        { "1k", "21:9", 1248, 528 },
        { "2k", "1:1", 2048, 2048 },
        { "2k", "16:9", 2048, 1152 },
        { "2k", "9:16", 1152, 2048 },
        { "2k", "4:3", 2048, 1536 },
        { "2k", "3:4", 1536, 2048 },
        { "2k", "3:2", 2048, 1360 },
        { "2k", "2:3", 1360, 2048 },
        { "2k", "21:9", 2048, 880 },
        { "4k", "1:1", 2880, 2880 },
        { "4k", "16:9", 3840, 2160 },
        { "4k", "9:16", 2160, 3840 },
        { "4k", "4:3", 3312, 2480 },
        { "4k", "3:4", 2480, 3312 },
        { "4k", "3:2", 3520, 2352 },
        { "4k", "2:3", 2352, 3520 },
        { "4k", "21:9", 3840, 1648 }
    };

    [Theory]
    [MemberData(nameof(SupportedSizes))]
    public async Task ResolveParametersAsync_ReturnsProviderCompliantSize(
        string resolutionCode,
        string aspectRatioCode,
        int expectedWidth,
        int expectedHeight)
    {
        using var context = new TestContext();

        var result = await context.Service.ResolveParametersAsync(new ResolveAiImageParametersRequest
        {
            ResolutionCode = resolutionCode,
            QualityCode = "med",
            AspectRatioCode = aspectRatioCode
        }, default);

        Assert.Equal(resolutionCode, result.ResolutionCode);
        Assert.Equal("med", result.QualityCode);
        Assert.Equal(aspectRatioCode, result.AspectRatioCode);
        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
        Assert.Equal($"{expectedWidth}x{expectedHeight}", result.Size);
        Assert.Equal("medium", result.ProviderQuality);

        Assert.NotNull(result.Width);
        Assert.NotNull(result.Height);
        var width = result.Width.Value;
        var height = result.Height.Value;
        Assert.Equal(0, width % 16);
        Assert.Equal(0, height % 16);
        Assert.InRange(width, 16, ProviderMaxEdge);
        Assert.InRange(height, 16, ProviderMaxEdge);

        var totalPixels = (long)width * height;
        Assert.InRange(totalPixels, ProviderMinPixels, ProviderMaxPixels);

        var longEdge = Math.Max(width, height);
        var shortEdge = Math.Min(width, height);
        Assert.True(longEdge <= shortEdge * 3, $"Resolved size {result.Size} exceeds the provider's 3:1 ratio limit.");
    }

    [Fact]
    public async Task ResolveParametersAsync_RejectsAutoAspectRatio()
    {
        using var context = new TestContext();

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            context.Service.ResolveParametersAsync(new ResolveAiImageParametersRequest
            {
                ResolutionCode = "4k",
                QualityCode = "high",
                AspectRatioCode = "auto"
            }, default));

        Assert.Equal(ErrorCodes.BadRequest, exception.Code);
        Assert.Equal(MachineErrorCodes.AutoSizeNotSupported, exception.MachineCode);
        Assert.Equal("当前站点配置不支持自动尺寸", exception.Message);
        Assert.Null(exception.Details);
    }

    private sealed class TestContext : IDisposable
    {
        private readonly HttpClient _httpClient;

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
                """);
            SeedParameters(Db);

            _httpClient = new HttpClient();
            Service = new AiImageService(
                _httpClient,
                Mock.Of<IAiImageModelConfigService>(),
                Mock.Of<IPointService>(),
                Db,
                Mock.Of<ICurrentUser>(),
                Mock.Of<IAiImageTaskQueue>(),
                Mock.Of<IAiImageAdmissionService>(),
                Options.Create(new OpenAiOptions()),
                Options.Create(new AiImageSizeModeOptions()),
                Options.Create(new PromptLibraryOptions()),
                Mock.Of<IAiMediaPathResolver>(),
                Mock.Of<IAiPromptFilter>(),
                Mock.Of<IUserConsentService>(),
                Mock.Of<IMediaAssetService>(),
                Mock.Of<IAiImageCatalogService>(),
                Mock.Of<IAiSizeModeRolloutPolicy>(),
                NullLogger<AiImageService>.Instance);
        }

        public SqlSugarClient Db { get; }

        public AiImageService Service { get; }

        public void Dispose()
        {
            _httpClient.Dispose();
            Db.Dispose();
        }

        private static void SeedParameters(SqlSugarClient db)
        {
            var createdAt = DateTime.UtcNow;
            var parameters = new[]
            {
                Parameter("resolution", "1k", "1K", null, 1024, null, 1, createdAt),
                Parameter("resolution", "2k", "2K", null, 2048, null, 2, createdAt),
                Parameter("resolution", "4k", "4K", null, 4096, null, 3, createdAt),
                Parameter("quality", "med", "Medium", "medium", null, null, 1, createdAt),
                Parameter("quality", "high", "High", "high", null, null, 2, createdAt),
                Parameter("aspect_ratio", "1:1", "1:1", null, 1, 1, 1, createdAt),
                Parameter("aspect_ratio", "16:9", "16:9", null, 16, 9, 2, createdAt),
                Parameter("aspect_ratio", "9:16", "9:16", null, 9, 16, 3, createdAt),
                Parameter("aspect_ratio", "4:3", "4:3", null, 4, 3, 4, createdAt),
                Parameter("aspect_ratio", "3:4", "3:4", null, 3, 4, 5, createdAt),
                Parameter("aspect_ratio", "3:2", "3:2", null, 3, 2, 6, createdAt),
                Parameter("aspect_ratio", "2:3", "2:3", null, 2, 3, 7, createdAt),
                Parameter("aspect_ratio", "21:9", "21:9", null, 21, 9, 8, createdAt)
            };

            db.Insertable(parameters).ExecuteCommand();
        }

        private static AiImageParameterEntity Parameter(
            string type,
            string code,
            string name,
            string? providerValue,
            int? valueInt1,
            int? valueInt2,
            int sort,
            DateTime createdAt)
        {
            return new AiImageParameterEntity
            {
                ParamType = type,
                ParamCode = code,
                ParamName = name,
                ProviderValue = providerValue,
                ValueInt1 = valueInt1,
                ValueInt2 = valueInt2,
                Sort = sort,
                Status = 1,
                CreatedAt = createdAt
            };
        }
    }
}
