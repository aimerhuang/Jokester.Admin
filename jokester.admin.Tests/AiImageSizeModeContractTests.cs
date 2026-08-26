using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.DTOs.NanoBananaImages;
using jokester.admin.Application.Security;
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

public sealed class AiImageSizeModeContractTests
{
    [Fact]
    public async Task ResolveAsync_AutoReturnsUnknownRequestedDimensionsAndIndependentPrice()
    {
        using var context = new CatalogContext();

        var result = await context.Service.ResolveAsync(
            new ResolveAiImageParametersRequest
            {
                ModelCode = "gpt-image-2",
                SizeMode = "auto",
                CatalogVersion = "imgcat_test_1",
                QualityCode = "med"
            },
            context.Client,
            default);

        Assert.Equal("auto", result.Parameters.Size);
        Assert.Equal("auto", result.Parameters.RequestedSize);
        Assert.Null(result.Parameters.ResolutionCode);
        Assert.Null(result.Parameters.AspectRatioCode);
        Assert.Null(result.Parameters.Width);
        Assert.Null(result.Parameters.Height);
        Assert.Equal(91, result.Price.Points);
        Assert.All(result.Routes, route => Assert.Equal("auto", route.SizeMode));
    }

    [Fact]
    public async Task Catalog_HidesAutoWhenAnyFallbackOperationIsUnverified()
    {
        using var context = new CatalogContext(addUnverifiedFallback: true);

        var models = await context.Service.GetModelsAsync(context.Client, default);
        var model = Assert.Single(models);
        Assert.Equal(["explicit"], model.Capabilities.SizeModes);
        Assert.False(model.Capabilities.SupportsAutoSize);

        var pricing = await context.Service.GetPricingAsync("gpt-image-2", "imgcat_test_1", context.Client, default);
        Assert.DoesNotContain(pricing.Items, item => item.SizeMode == "auto");
    }

    [Fact]
    public async Task ResolveAsync_RejectsStaleCatalogBeforePricingOrRouting()
    {
        using var context = new CatalogContext();

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.ResolveAsync(
            new ResolveAiImageParametersRequest
            {
                ModelCode = "gpt-image-2",
                SizeMode = "auto",
                CatalogVersion = "imgcat_stale",
                QualityCode = "med"
            },
            context.Client,
            default));

        Assert.Equal(ErrorCodes.Conflict, exception.Code);
        Assert.Equal(MachineErrorCodes.ImageCatalogChanged, exception.MachineCode);
    }

    [Fact]
    public async Task NanoCompatibilityEndpoint_RejectsSizeModeContractFields()
    {
        using var httpClient = new HttpClient();
        var service = new NanoBananaImageService(
            httpClient,
            Mock.Of<IAiImageModelConfigService>(),
            Mock.Of<IPointService>(),
            Mock.Of<ISqlSugarClient>(),
            Mock.Of<ICurrentUser>(),
            Mock.Of<IAiImageTaskQueue>(),
            Mock.Of<IAiImageAdmissionService>(),
            Options.Create(new PromptLibraryOptions()),
            Mock.Of<IAiMediaPathResolver>(),
            Mock.Of<IAiPromptFilter>(),
            Mock.Of<IUserConsentService>(),
            Mock.Of<IMediaAssetService>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.CreateAsync(
            new CreateNanoBananaImageTaskRequest
            {
                SizeMode = "auto",
                CatalogVersion = "imgcat_test_1",
                IdempotencyKey = Guid.NewGuid().ToString(),
                Prompt = "test"
            },
            default));

        Assert.Equal(MachineErrorCodes.InvalidSizeModeCombination, exception.MachineCode);
    }

    [Fact]
    public async Task DurableReplay_ReturnsOrderedAliasesAndProjectsSoftDeletionWithoutLiveDependencies()
    {
        using var context = new DurableReplayContext();
        var request = context.CreateRequest();

        var active = await context.Service.CreateTasksResponseAsync(request, default);
        Assert.Equal("active", active.RequestState);
        Assert.Equal(101, active.Id);
        Assert.Equal(active.Id, active.TaskId);
        Assert.Equal([101L, 102L], active.Ids);
        Assert.Equal(active.Ids, active.TaskIds);

        context.SetDeleted(101, true);
        var partial = await context.Service.CreateTasksResponseAsync(request, default);
        Assert.Equal("partially_deleted", partial.RequestState);
        Assert.Equal([101L, 102L], partial.Ids);

        context.SetDeleted(102, true);
        var deleted = await context.Service.CreateTasksResponseAsync(request, default);
        Assert.Equal("deleted", deleted.RequestState);
        Assert.Equal([101L, 102L], deleted.TaskIds);

        context.Catalog.VerifyNoOtherCalls();
        context.Points.VerifyNoOtherCalls();
        context.Admission.VerifyNoOtherCalls();
    }

    private sealed class DurableReplayContext : IDisposable
    {
        private const long UserId = 7;
        private const string IdempotencyKey = "durable-replay-key";

        public DurableReplayContext()
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
                CREATE TABLE ai_image_request_idempotency (
                    id INTEGER PRIMARY KEY,
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
                    PRIMARY KEY (request_id, task_ordinal)
                );
                CREATE TABLE ai_image_task (
                    id INTEGER PRIMARY KEY,
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );
                """);

            var request = CreateRequest();
            var identity = AiImageIdempotency.Create(IdempotencyKey, new
            {
                Endpoint = "ai-images-size-mode-v1",
                SourcePromptId = (long?)null,
                Prompt = "durable replay",
                NegativePrompt = (string?)null,
                ModelCode = "gpt-image-2",
                ImageCount = 2,
                SizeMode = "auto",
                QualityCode = "med",
                ResolutionCode = (string?)null,
                AspectRatioCode = (string?)null,
                ReferenceAssetIds = Array.Empty<string>(),
                ReferenceImageUrls = Array.Empty<string>(),
                MaskAssetId = (string?)null,
                MaskImageUrl = (string?)null
            });
            Db.Insertable(new AiImageRequestEntity
            {
                Id = 1,
                UserId = UserId,
                IdempotencyKeyHash = identity.KeyHash,
                CanonicalPayloadHash = identity.RequestFingerprint,
                CanonicalizationVersion = AiImageCatalogService.SizeContractVersion,
                NormalizationProfile = "native-v1",
                SizeContractVersion = AiImageCatalogService.SizeContractVersion,
                ModelReleaseId = 9,
                RequestedImageCount = 2,
                TaskCount = 2,
                LegacyBatchShape = "split-task-per-image",
                Status = "active",
                CreatedAt = DateTime.Now
            }).ExecuteCommand();
            Db.Ado.ExecuteCommand("INSERT INTO ai_image_task (id, is_deleted) VALUES (101, 0), (102, 0)");
            Db.Insertable(new[]
            {
                new AiImageRequestTaskEntity { RequestId = 1, TaskOrdinal = 0, TaskId = 101 },
                new AiImageRequestTaskEntity { RequestId = 1, TaskOrdinal = 1, TaskId = 102 }
            }).ExecuteCommand();

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(UserId);
            Catalog = new Mock<IAiImageCatalogService>(MockBehavior.Strict);
            Points = new Mock<IPointService>(MockBehavior.Strict);
            Admission = new Mock<IAiImageAdmissionService>(MockBehavior.Strict);
            Service = new AiImageService(
                new HttpClient(),
                Mock.Of<IAiImageModelConfigService>(),
                Points.Object,
                Db,
                currentUser.Object,
                Mock.Of<IAiImageTaskQueue>(),
                Admission.Object,
                Options.Create(new OpenAiOptions()),
                Options.Create(new AiImageSizeModeOptions()),
                Options.Create(new PromptLibraryOptions()),
                Mock.Of<IAiMediaPathResolver>(),
                Mock.Of<IAiPromptFilter>(),
                Mock.Of<IUserConsentService>(),
                Mock.Of<IMediaAssetService>(),
                Catalog.Object,
                Mock.Of<IAiSizeModeRolloutPolicy>(),
                NullLogger<AiImageService>.Instance);
        }

        public SqlSugarClient Db { get; }

        public AiImageService Service { get; }

        public Mock<IAiImageCatalogService> Catalog { get; }

        public Mock<IPointService> Points { get; }

        public Mock<IAiImageAdmissionService> Admission { get; }

        public CreateAiImageTaskRequest CreateRequest() => new()
        {
            IdempotencyKey = IdempotencyKey,
            Prompt = "durable replay",
            ModelCode = "gpt-image-2",
            ImageCount = 2,
            SizeMode = "auto",
            QualityCode = "med",
            CatalogVersion = "imgcat_stale_after_creation"
        };

        public void SetDeleted(long taskId, bool deleted) => Db.Ado.ExecuteCommand(
            "UPDATE ai_image_task SET is_deleted = @deleted WHERE id = @id",
            new SugarParameter("@deleted", deleted ? 1 : 0),
            new SugarParameter("@id", taskId));

        public void Dispose() => Db.Dispose();
    }

    private sealed class CatalogContext : IDisposable
    {
        public CatalogContext(bool addUnverifiedFallback = false)
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
            Seed(addUnverifiedFallback);

            var legacyModels = new Mock<IAiImageModelConfigService>();
            legacyModels.Setup(x => x.GetEnabledModelsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new AiImageModelOptionDto
                    {
                        Code = "gpt-image-2",
                        Name = "GPT Image 2",
                        Provider = AiImageModelConfigService.OpenAiImageProtocol,
                        ProviderCode = "openai",
                        Resolutions = ["1k"],
                        Qualities = ["med"],
                        AspectRatios = ["1:1"],
                        Capabilities = new AiImageModelCapabilitiesDto { SupportsQuality = true }
                    }
                ]);
            var rollout = new Mock<IAiSizeModeRolloutPolicy>();
            rollout.Setup(x => x.CanUseVersionedContract(It.IsAny<AiImageClientContext>())).Returns(true);
            rollout.Setup(x => x.CanUseAuto(It.IsAny<AiImageClientContext>(), "gpt-image-2", "imgcat_test_1")).Returns(true);
            Client = new AiImageClientContext(7, true, "web", "1.0.0", "1");
            Service = new AiImageCatalogService(
                Db,
                legacyModels.Object,
                rollout.Object,
                Options.Create(new AiImageSizeModeOptions
                {
                    Enabled = true,
                    AutoEnabled = true,
                    ProviderAllowedHosts = ["provider.example"],
                    ResultAllowedHosts = ["images.example"]
                }));
        }

        public SqlSugarClient Db { get; }

        public AiImageCatalogService Service { get; }

        public AiImageClientContext Client { get; }

        private void CreateSchema()
        {
            Db.Ado.ExecuteCommand("""
                CREATE TABLE ai_image_model_release (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, model_code TEXT NOT NULL, model_name TEXT NOT NULL,
                    catalog_version TEXT NOT NULL, size_contract_version TEXT NOT NULL, default_size_mode TEXT NOT NULL,
                    status TEXT NOT NULL, revoked_at TEXT NULL, created_at TEXT NOT NULL, published_at TEXT NULL);
                CREATE TABLE ai_image_model_current_release (
                    model_code TEXT PRIMARY KEY, model_release_id INTEGER NOT NULL, updated_at TEXT NOT NULL);
                CREATE TABLE ai_image_model_release_route (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, model_release_id INTEGER NOT NULL, route_config_id INTEGER NOT NULL,
                    size_mode TEXT NOT NULL, resolution_code TEXT NOT NULL, route_role TEXT NOT NULL,
                    provider_protocol TEXT NOT NULL, consent_provider_code TEXT NOT NULL, provider_model TEXT NOT NULL,
                    base_url TEXT NOT NULL, text_to_image_path TEXT NOT NULL, image_to_image_path TEXT NOT NULL,
                    secret_version_hash TEXT NOT NULL, verified_generations INTEGER NOT NULL, verified_edits INTEGER NOT NULL,
                    verified_mask_edits INTEGER NOT NULL, sort INTEGER NOT NULL);
                CREATE TABLE ai_image_model_release_price (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, model_release_id INTEGER NOT NULL, model_code TEXT NOT NULL,
                    pricing_mode TEXT NOT NULL, resolution_code TEXT NOT NULL, quality_code TEXT NOT NULL,
                    points INTEGER NOT NULL, price_amount NUMERIC NOT NULL, currency TEXT NOT NULL,
                    sort INTEGER NOT NULL, status INTEGER NOT NULL);
                CREATE TABLE ai_image_model_config (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, model_code TEXT NOT NULL, model_name TEXT NOT NULL,
                    provider TEXT NOT NULL, provider_model TEXT NOT NULL, resolution_code TEXT NOT NULL,
                    route_role TEXT NOT NULL, base_url TEXT NOT NULL, api_key TEXT NOT NULL,
                    text_to_image_path TEXT NOT NULL, image_to_image_path TEXT NOT NULL,
                    sort INTEGER NOT NULL, status INTEGER NOT NULL, created_at TEXT NOT NULL,
                    updated_at TEXT NULL, is_deleted INTEGER NOT NULL);
                CREATE TABLE ai_image_parameter (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, param_type TEXT NOT NULL, param_code TEXT NOT NULL,
                    param_name TEXT NOT NULL, provider_value TEXT NULL, value_int_1 INTEGER NULL,
                    value_int_2 INTEGER NULL, sort INTEGER NOT NULL, status INTEGER NOT NULL,
                    created_at TEXT NOT NULL, updated_at TEXT NULL, is_deleted INTEGER NOT NULL DEFAULT 0);
                """);
        }

        private void Seed(bool addUnverifiedFallback)
        {
            const string apiKey = "secret-test-key";
            var release = new AiImageModelReleaseEntity
            {
                ModelCode = "gpt-image-2",
                ModelName = "GPT Image 2",
                CatalogVersion = "imgcat_test_1",
                SizeContractVersion = AiImageCatalogService.SizeContractVersion,
                DefaultSizeMode = "explicit",
                Status = "published",
                CreatedAt = DateTime.UtcNow,
                PublishedAt = DateTime.UtcNow
            };
            release.Id = Db.Insertable(release).ExecuteReturnBigIdentity();
            Db.Insertable(new AiImageCurrentReleaseEntity
            {
                ModelCode = release.ModelCode,
                ModelReleaseId = release.Id,
                UpdatedAt = DateTime.UtcNow
            }).ExecuteCommand();
            var primary = InsertConfig("primary", apiKey, 1);
            InsertRoute(release.Id, primary, "explicit", "1k", "primary", true, apiKey);
            InsertRoute(release.Id, primary, "auto", "", "primary", true, apiKey);
            if (addUnverifiedFallback)
            {
                var fallback = InsertConfig("fallback", apiKey, 2);
                InsertRoute(release.Id, fallback, "auto", "", "fallback", false, apiKey);
            }
            Db.Insertable(new[]
            {
                new AiImageModelReleasePriceEntity
                {
                    ModelReleaseId = release.Id,
                    ModelCode = release.ModelCode,
                    PricingMode = "explicit",
                    ResolutionCode = "1k",
                    QualityCode = "med",
                    Points = 15,
                    PriceAmount = 0.15m,
                    Currency = "CNY",
                    Sort = 1,
                    Status = 1
                },
                new AiImageModelReleasePriceEntity
                {
                    ModelReleaseId = release.Id,
                    ModelCode = release.ModelCode,
                    PricingMode = "auto",
                    ResolutionCode = "",
                    QualityCode = "med",
                    Points = 91,
                    PriceAmount = 0.91m,
                    Currency = "CNY",
                    Sort = 2,
                    Status = 1
                }
            }).ExecuteCommand();
            Db.Insertable(new[]
            {
                Parameter("resolution", "1k", 1024, null),
                Parameter("quality", "med", null, null, "medium"),
                Parameter("aspect_ratio", "1:1", 1, 1)
            }).ExecuteCommand();
        }

        private long InsertConfig(string role, string apiKey, int sort)
        {
            return Db.Insertable(new AiImageModelConfigEntity
            {
                ModelCode = "gpt-image-2",
                ModelName = "GPT Image 2",
                Provider = AiImageModelConfigService.OpenAiImageProtocol,
                ProviderModel = "gpt-image-2",
                ResolutionCode = "",
                RouteRole = role,
                BaseUrl = "https://provider.example/v1",
                ApiKey = apiKey,
                TextToImagePath = "/images/generations",
                ImageToImagePath = "/images/edits",
                Sort = sort,
                Status = 1,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            }).ExecuteReturnBigIdentity();
        }

        private void InsertRoute(long releaseId, long configId, string mode, string resolution, string role, bool verified, string apiKey)
        {
            Db.Insertable(new AiImageModelReleaseRouteEntity
            {
                ModelReleaseId = releaseId,
                RouteConfigId = configId,
                SizeMode = mode,
                ResolutionCode = resolution,
                RouteRole = role,
                ProviderProtocol = AiImageModelConfigService.OpenAiImageProtocol,
                ConsentProviderCode = "openai",
                ProviderModel = "gpt-image-2",
                BaseUrl = "https://provider.example/v1",
                TextToImagePath = "/images/generations",
                ImageToImagePath = "/images/edits",
                SecretVersionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))).ToLowerInvariant(),
                VerifiedGenerations = verified,
                VerifiedEdits = verified,
                VerifiedMaskEdits = verified,
                Sort = role == "primary" ? 1 : 2
            }).ExecuteCommand();
        }

        private static AiImageParameterEntity Parameter(string type, string code, int? value1, int? value2, string? providerValue = null) => new()
        {
            ParamType = type,
            ParamCode = code,
            ParamName = code,
            ProviderValue = providerValue,
            ValueInt1 = value1,
            ValueInt2 = value2,
            Sort = 1,
            Status = 1,
            CreatedAt = DateTime.UtcNow
        };

        public void Dispose() => Db.Dispose();
    }
}
