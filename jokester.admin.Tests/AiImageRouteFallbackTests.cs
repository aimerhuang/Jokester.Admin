using System.Net;
using System.Net.Http.Headers;
using System.Text;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.Services;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiImageRouteFallbackTests
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    [Fact]
    public async Task GenerateFromResolved_SendsExplicit2KSize_AndUsesFallbackRoute_WhenPrimaryFails()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"jokester-ai-route-test-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(testRoot, "app");
        var mediaRoot = Path.Combine(testRoot, "persistent-media");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var handler = new SequenceHandler(
                _ => new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent("{\"error\":\"primary unavailable\"}", Encoding.UTF8, "application/json")
                },
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"data\":[{{\"b64_json\":\"{OnePixelPng}\"}}]}}", Encoding.UTF8, "application/json")
                });
            using var httpClient = new HttpClient(handler);

            var modelConfigService = new Mock<IAiImageModelConfigService>();
            modelConfigService
                .Setup(x => x.ResolveRoutesAsync("gpt-image-2", "2k", It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new ResolvedAiImageModelConfig
                    {
                        Id = 10,
                        ModelCode = "gpt-image-2",
                        ModelName = "GPT Image 2",
                        Provider = "primary-openai-image",
                        ProviderModel = "gpt-image-2",
                        ResolutionCode = "2k",
                        RouteRole = AiImageModelConfigService.PrimaryRouteRole,
                        BaseUrl = "https://primary.example/v1",
                        ApiKey = "primary-key",
                        TextToImagePath = "/images/generations",
                        ImageToImagePath = "/images/edits"
                    },
                    new ResolvedAiImageModelConfig
                    {
                        Id = 11,
                        ModelCode = "gpt-image-2",
                        ModelName = "GPT Image 2",
                        Provider = "fallback-openai-image",
                        ProviderModel = "configured-fallback-model",
                        ResolutionCode = "2k",
                        RouteRole = AiImageModelConfigService.FallbackRouteRole,
                        BaseUrl = "https://fallback.example/v1",
                        ApiKey = "fallback-key",
                        TextToImagePath = "/images/generations",
                        ImageToImagePath = "/images/edits"
                    }
                ]);
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(contentRoot);
            var mediaPathResolver = new AiMediaPathResolver(
                Options.Create(new AiMediaStorageOptions { RootPath = mediaRoot }),
                environment.Object,
                NullLogger<AiMediaPathResolver>.Instance);
            var promptFilter = new Mock<IAiPromptFilter>();
            promptFilter
                .Setup(x => x.EnsureAllowedAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new jokester.admin.Application.Models.AiPromptFilter.AiPromptFilterResult(true, 1, null));

            var service = new AiImageService(
                httpClient,
                modelConfigService.Object,
                Mock.Of<IPointService>(),
                Mock.Of<ISqlSugarClient>(),
                Mock.Of<ICurrentUser>(),
                Mock.Of<IAiImageTaskQueue>(),
                Mock.Of<IAiImageAdmissionService>(),
                Options.Create(new OpenAiOptions
                {
                    PrimaryTimeoutSeconds = 180
                }),
                Options.Create(new AiImageSizeModeOptions()),
                Options.Create(new PromptLibraryOptions()),
                mediaPathResolver,
                promptFilter.Object,
                Mock.Of<IUserConsentService>(),
                Mock.Of<IMediaAssetService>(),
                Mock.Of<IAiImageCatalogService>(),
                Mock.Of<IAiSizeModeRolloutPolicy>(),
                NullLogger<AiImageService>.Instance);

            var result = await service.GenerateFromResolvedAsync(
                "test image",
                "gpt-image-2",
                new ResolveAiImageParametersResponse
                {
                    ResolutionCode = "2k",
                    QualityCode = "med",
                    AspectRatioCode = "16:9",
                    Width = 2048,
                    Height = 1152,
                    Size = "2048x1152",
                    ProviderQuality = "medium"
                },
                [],
                null,
                42,
                default);

            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal("https://primary.example/v1/images/generations", handler.Requests[0].Uri);
            Assert.Equal("primary-key", handler.Requests[0].BearerToken);
            Assert.Equal("gpt-image-2", handler.Requests[0].Model);
            Assert.Equal("2048x1152", handler.Requests[0].Size);
            Assert.Equal("medium", handler.Requests[0].Quality);
            Assert.Equal(1, handler.Requests[0].ImageCount);
            Assert.Equal("https://fallback.example/v1/images/generations", handler.Requests[1].Uri);
            Assert.Equal("fallback-key", handler.Requests[1].BearerToken);
            Assert.Equal("configured-fallback-model", handler.Requests[1].Model);
            Assert.Equal("2048x1152", handler.Requests[1].Size);
            Assert.Equal("medium", handler.Requests[1].Quality);
            Assert.Equal(1, handler.Requests[1].ImageCount);
            Assert.Equal("configured-fallback-model", result.ProviderModel);
            Assert.StartsWith("/api/media/ai/42/", result.Url, StringComparison.Ordinal);
            var storedFile = mediaPathResolver.ResolveFilePath(result.Url["/api/media/ai/".Length..]);
            Assert.True(File.Exists(storedFile));
            Assert.False(Directory.Exists(Path.Combine(contentRoot, "private-media", "ai")));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateFromRelease_SendsAutoSize_ForJsonAndMultipartRequests()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"jokester-ai-auto-route-test-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(testRoot, "app");
        var mediaRoot = Path.Combine(testRoot, "persistent-media");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var success = new Func<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"data\":[{{\"b64_json\":\"{OnePixelPng}\"}}]}}", Encoding.UTF8, "application/json")
            });
            var handler = new SequenceHandler(success, success);
            using var httpClient = new HttpClient(handler);
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(contentRoot);
            var mediaPathResolver = new AiMediaPathResolver(
                Options.Create(new AiMediaStorageOptions { RootPath = mediaRoot }),
                environment.Object,
                NullLogger<AiMediaPathResolver>.Instance);
            var service = new AiImageService(
                httpClient,
                Mock.Of<IAiImageModelConfigService>(),
                Mock.Of<IPointService>(),
                Mock.Of<ISqlSugarClient>(),
                Mock.Of<ICurrentUser>(),
                Mock.Of<IAiImageTaskQueue>(),
                Mock.Of<IAiImageAdmissionService>(),
                Options.Create(new OpenAiOptions()),
                Options.Create(new AiImageSizeModeOptions()),
                Options.Create(new PromptLibraryOptions()),
                mediaPathResolver,
                Mock.Of<IAiPromptFilter>(),
                Mock.Of<IUserConsentService>(),
                Mock.Of<IMediaAssetService>(),
                Mock.Of<IAiImageCatalogService>(),
                Mock.Of<IAiSizeModeRolloutPolicy>(),
                NullLogger<AiImageService>.Instance);
            var route = new ResolvedAiImageModelConfig
            {
                Id = 20,
                ModelReleaseId = 3,
                ReleaseRouteId = 4,
                CatalogVersion = "imgcat_auto_test",
                SizeMode = "auto",
                ModelCode = "gpt-image-2",
                ModelName = "GPT Image 2",
                ProviderModel = "gpt-image-2",
                RouteRole = "primary",
                BaseUrl = "https://provider.example/v1",
                ApiKey = "test-key",
                TextToImagePath = "/images/generations",
                ImageToImagePath = "/images/edits"
            };
            var parameters = new ResolveAiImageParametersResponse
            {
                SizeContractVersion = AiImageCatalogService.SizeContractVersion,
                ModelCode = "gpt-image-2",
                SizeMode = "auto",
                CatalogVersion = "imgcat_auto_test",
                RequestedSize = "auto",
                QualityCode = "med",
                Size = "auto",
                ProviderQuality = "medium"
            };

            await service.GenerateFromResolvedRoutesAsync("text image", [route], parameters, [], null, 42, default);
            var inputKey = "42/assets/reference.png";
            var inputPath = mediaPathResolver.ResolveFilePath(inputKey);
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            await File.WriteAllBytesAsync(inputPath, Convert.FromBase64String(OnePixelPng));
            await service.GenerateFromResolvedRoutesAsync(
                "edit image",
                [route],
                parameters,
                [$"/api/media/ai/{inputKey}"],
                null,
                42,
                default);

            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, request => Assert.Equal("auto", request.Size));
            Assert.Equal("application/json", handler.Requests[0].ContentType);
            Assert.StartsWith("multipart/form-data", handler.Requests[1].ContentType, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int requestIndex;

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var model = string.Empty;
            var size = string.Empty;
            var quality = string.Empty;
            var imageCount = 0;
            if (!string.IsNullOrWhiteSpace(body)
                && request.Content?.Headers.ContentType?.MediaType == "application/json")
            {
                using var document = System.Text.Json.JsonDocument.Parse(body);
                model = document.RootElement.GetProperty("model").GetString() ?? string.Empty;
                size = document.RootElement.GetProperty("size").GetString() ?? string.Empty;
                quality = document.RootElement.GetProperty("quality").GetString() ?? string.Empty;
                imageCount = document.RootElement.GetProperty("n").GetInt32();
            }
            else if (request.Content is MultipartContent multipart)
            {
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    var value = name is "model" or "size" or "quality" or "n"
                        ? await part.ReadAsStringAsync(cancellationToken)
                        : string.Empty;
                    if (name == "model") model = value;
                    if (name == "size") size = value;
                    if (name == "quality") quality = value;
                    if (name == "n" && int.TryParse(value, out var parsed)) imageCount = parsed;
                }
            }

            Requests.Add(new CapturedRequest(
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.Scheme == "Bearer"
                    ? request.Headers.Authorization.Parameter
                    : null,
                model,
                size,
                quality,
                imageCount,
                request.Content?.Headers.ContentType?.MediaType ?? string.Empty));
            var response = responses[requestIndex++](request);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed record CapturedRequest(
        string Uri,
        string? BearerToken,
        string Model,
        string Size,
        string Quality,
        int ImageCount,
        string ContentType);
}
