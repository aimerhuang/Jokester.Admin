using System.Net;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Security;
using jokester.admin.Application.Services;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class NanoBananaOpenAiImagesRouteTests : IDisposable
{
    private readonly string mediaRoot = Path.Combine(Path.GetTempPath(), $"nano-openai-{Guid.NewGuid():N}");

    [Fact]
    public async Task OpenAiImagesRoute_PostsGenerationRequestAndDownloadsResultUrl()
    {
        var png = await CreatePngAsync();
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Post
            ? Json("""{"data":[{"url":"https://result.example.com/jobs/1/0.png"}]}""")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
        var service = CreateService(handler, "/v1/images/generations", "/v1/images/edits");

        var result = await service.GenerateFromTaskAsync("a red apple", "nano-banana-pro", "2k", "16:9", "2048x1152", [], 7, default);

        Assert.Equal(2, handler.Requests.Count);
        var post = handler.Requests[0];
        Assert.Equal("https://relay.example.com/v1/images/generations", post.Uri);
        Assert.Equal("Bearer", post.AuthScheme);
        using var body = JsonDocument.Parse(post.Body!);
        Assert.Equal("gemini-3-pro-image", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("2048x1152", body.RootElement.GetProperty("size").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("n").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("contents", out _));

        Assert.Equal("https://result.example.com/jobs/1/0.png", handler.Requests[1].Uri);
        Assert.Null(handler.Requests[1].AuthScheme);
        Assert.StartsWith("/api/media/ai/7/", result.Url);
        Assert.True(File.Exists(Path.Combine(mediaRoot, result.Url["/api/media/ai/".Length..])));
    }

    [Fact]
    public async Task GeminiNativeRoute_KeepsGenerateContentBody()
    {
        var png = await CreatePngAsync();
        var geminiBody = JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { inlineData = new { mimeType = "image/png", data = Convert.ToBase64String(png) } } } } } }
        });
        var handler = new RecordingHandler(_ => Json(geminiBody));
        var service = CreateService(handler, "/v1beta/models/{model}:generateContent", "/v1beta/models/{model}:generateContent");

        await service.GenerateFromTaskAsync("a red apple", "nano-banana-pro", "1k", "1:1", "1024x1024", [], 7, default);

        var post = Assert.Single(handler.Requests);
        Assert.Equal("https://relay.example.com/v1beta/models/gemini-3-pro-image:generateContent", post.Uri);
        using var body = JsonDocument.Parse(post.Body!);
        Assert.True(body.RootElement.TryGetProperty("contents", out _));
        Assert.Equal("1:1", body.RootElement.GetProperty("generationConfig").GetProperty("imageConfig").GetProperty("aspectRatio").GetString());
    }

    [Fact]
    public async Task OpenAiImagesRoute_RejectsNonHttpsResultUrlWithoutDownloading()
    {
        var handler = new RecordingHandler(_ => Json("""{"data":[{"url":"http://127.0.0.1/0.png"}]}"""));
        var service = CreateService(handler, "/v1/images/generations", "/v1/images/edits");

        await Assert.ThrowsAsync<jokester.admin.Common.Exceptions.AppException>(() =>
            service.GenerateFromTaskAsync("a red apple", "nano-banana-pro", "1k", "1:1", "1024x1024", [], 7, default));
        Assert.Single(handler.Requests);
    }

    public void Dispose()
    {
        if (Directory.Exists(mediaRoot))
        {
            Directory.Delete(mediaRoot, recursive: true);
        }
    }

    private NanoBananaImageService CreateService(RecordingHandler handler, string textPath, string imagePath)
    {
        var config = new Mock<IAiImageModelConfigService>();
        config.Setup(x => x.ResolveAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedAiImageModelConfig
            {
                Id = 3,
                ModelCode = "nano-banana-pro",
                ModelName = "Nano Banana Pro",
                Provider = "gemini-image",
                ProviderProtocol = AiImageModelConfigService.GeminiImageProtocol,
                ConsentProviderCode = "google",
                ProviderModel = "gemini-3-pro-image",
                RouteRole = "primary",
                BaseUrl = "https://relay.example.com",
                ApiKey = "test-key",
                TextToImagePath = textPath,
                ImageToImagePath = imagePath
            });
        var paths = new Mock<IAiMediaPathResolver>();
        paths.Setup(x => x.ResolveFilePath(It.IsAny<string>()))
            .Returns<string>(relative => Path.Combine(mediaRoot, relative));

        return new NanoBananaImageService(
            new HttpClient(handler),
            config.Object,
            Mock.Of<IPointService>(),
            Mock.Of<ISqlSugarClient>(),
            Mock.Of<ICurrentUser>(),
            Mock.Of<IAiImageTaskQueue>(),
            Mock.Of<IAiImageAdmissionService>(),
            Options.Create(new PromptLibraryOptions()),
            paths.Object,
            Mock.Of<IAiPromptFilter>(),
            Mock.Of<IMediaAssetService>(),
            Options.Create(new AiImageSizeModeOptions()),
            NullLogger<NanoBananaImageService>.Instance);
    }

    private static async Task<byte[]> CreatePngAsync()
    {
        using var image = new Image<Rgba32>(4, 4);
        await using var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        return stream.ToArray();
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed record RecordedRequest(string Uri, string? AuthScheme, string? Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.RequestUri!.ToString(), request.Headers.Authorization?.Scheme, body));
            return respond(request);
        }
    }
}
