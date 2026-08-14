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
    public async Task GenerateFromResolved_UsesFallbackRoute_WhenPrimaryRouteFails()
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
                .Setup(x => x.ResolveRoutesAsync("gpt-image-2", "1k", It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new ResolvedAiImageModelConfig
                    {
                        Id = 10,
                        ModelCode = "gpt-image-2",
                        ModelName = "GPT Image 2",
                        Provider = "primary-openai-image",
                        ProviderModel = "gpt-image-2",
                        ResolutionCode = "1k",
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
                        ProviderModel = "gpt-image-2-1k",
                        ResolutionCode = "1k",
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
                    PrimaryTimeoutSeconds = 120
                }),
                Options.Create(new PromptLibraryOptions()),
                mediaPathResolver,
                promptFilter.Object,
                Mock.Of<IUserConsentService>(),
                Mock.Of<IMediaAssetService>(),
                NullLogger<AiImageService>.Instance);

            var result = await service.GenerateFromResolvedAsync(
                "test image",
                "gpt-image-2",
                new ResolveAiImageParametersResponse
                {
                    ResolutionCode = "1k",
                    QualityCode = "med",
                    AspectRatioCode = "1:1",
                    Width = 1024,
                    Height = 1024,
                    Size = "1024x1024",
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
            Assert.Equal("https://fallback.example/v1/images/generations", handler.Requests[1].Uri);
            Assert.Equal("fallback-key", handler.Requests[1].BearerToken);
            Assert.Equal("gpt-image-2-1k", handler.Requests[1].Model);
            Assert.Equal("gpt-image-2-1k", result.ProviderModel);
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
            if (!string.IsNullOrWhiteSpace(body)
                && request.Content?.Headers.ContentType?.MediaType == "application/json")
            {
                using var document = System.Text.Json.JsonDocument.Parse(body);
                model = document.RootElement.GetProperty("model").GetString() ?? string.Empty;
            }

            Requests.Add(new CapturedRequest(
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.Scheme == "Bearer"
                    ? request.Headers.Authorization.Parameter
                    : null,
                model));
            var response = responses[requestIndex++](request);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed record CapturedRequest(string Uri, string? BearerToken, string Model);
}
