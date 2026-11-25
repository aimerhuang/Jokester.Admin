using System.Net;
using System.Text;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Security;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiImageGifProviderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task NativeRouteSendsGifOptionsAndPreservesValidatedAnimation(bool withReference, bool useResultUrl)
    {
        var root = Directory.CreateTempSubdirectory("jokester-gif-");
        try
        {
            var bytes = Animation(2, 20);
            var requests = 0;
            using var handler = new Handler(async request =>
            {
                requests++;
                if (request.Method == HttpMethod.Get)
                {
                    Assert.Null(request.Headers.Authorization);
                    Assert.Equal("https://cdn.example/animation.gif", request.RequestUri!.AbsoluteUri);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                }
                Assert.Equal("test-secret", request.Headers.Authorization!.Parameter);
                if (withReference)
                {
                    Assert.Equal("/images/edits", request.RequestUri!.AbsolutePath);
                    var content = Assert.IsType<MultipartFormDataContent>(request.Content);
                    var fields = content.ToDictionary(item => item.Headers.ContentDisposition!.Name!.Trim('"'));
                    Assert.Contains("image[]", fields.Keys);
                    Assert.Equal("gif", await fields["output_format"].ReadAsStringAsync());
                    Assert.Equal("2", await fields["frame_count"].ReadAsStringAsync());
                    Assert.Equal("200", await fields["frame_duration_ms"].ReadAsStringAsync());
                    Assert.Equal("native-test", await fields["model"].ReadAsStringAsync());
                }
                else
                {
                    Assert.Equal("/images/generations", request.RequestUri!.AbsolutePath);
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                    Assert.Equal("gif", body.RootElement.GetProperty("output_format").GetString());
                    Assert.Equal(2, body.RootElement.GetProperty("frame_count").GetInt32());
                    Assert.Equal(200, body.RootElement.GetProperty("frame_duration_ms").GetInt32());
                    Assert.Equal("native-test", body.RootElement.GetProperty("model").GetString());
                }
                var data = useResultUrl ? JsonSerializer.Serialize(new { url = "https://cdn.example/animation.gif" })
                    : JsonSerializer.Serialize(new { b64_json = Convert.ToBase64String(bytes) });
                var usage = withReference ? """{"input_tokens":1024,"output_tokens":439}"""
                    : """{"input_tokens_details":{"text_tokens":26,"image_tokens":0},"output_tokens":439}""";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    $$"""{"data":[{{data}}],"usage":{{usage}}}""", Encoding.UTF8, "application/json") };
            });
            using var client = new HttpClient(handler);
            var paths = new Mock<IAiMediaPathResolver>();
            paths.Setup(x => x.ResolveFilePath(It.IsAny<string>())).Returns<string>(path => Path.Combine(root.FullName, path));
            var service = new AiImageService(client, Mock.Of<IAiImageModelConfigService>(), Mock.Of<IPointService>(),
                Mock.Of<ISqlSugarClient>(), Mock.Of<ICurrentUser>(), Mock.Of<IAiImageTaskQueue>(), Mock.Of<IAiImageAdmissionService>(),
                Options.Create(new OpenAiOptions()), Options.Create(new AiImageSizeModeOptions { ResultAllowedHosts = ["cdn.example"] }),
                Options.Create(new PromptLibraryOptions()), paths.Object, Mock.Of<IAiPromptFilter>(), Mock.Of<IMediaAssetService>(),
                Mock.Of<IAiImageCatalogService>(), Mock.Of<IAiSizeModeRolloutPolicy>(), NullLogger<AiImageService>.Instance);
            Directory.CreateDirectory(Path.Combine(root.FullName, "42"));
            await File.WriteAllBytesAsync(Path.Combine(root.FullName, "42", "ref.png"), [1, 2, 3]);
            var task = new AiImageTaskEntity
            {
                UserId = 42, OutputFormat = "gif", FrameCount = 2, FrameDurationMs = 200, Prompt = "画一片叶子", Quality = "low",
                Width = 16, Height = 16, Size = "16x16", ReferenceImageUrls = withReference ? """["/api/media/ai/42/ref.png"]""" : null
            };
            var (url, result) = await service.GenerateGifFromTaskAsync(task, [new ResolvedAiImageModelConfig
            {
                ApiKey = "test-secret", BaseUrl = "https://provider.example", TextToImagePath = "/images/generations",
                ImageToImagePath = "/images/edits", ProviderModel = "native-test"
            }], default);
            Assert.Equal(useResultUrl ? 2 : 1, requests);
            Assert.StartsWith("/api/media/ai/42/", url);
            Assert.EndsWith(".gif", url);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(paths.Object.ResolveFilePath(url["/api/media/ai/".Length..])));
            Assert.Equal(new GifTaskResult(16, 16, withReference ? new GifTokenUsage(1024, null, null, 439)
                : new GifTokenUsage(26, 26, 0, 439)), result);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RejectsWrongFrameCountDelayDimensionsAndTruncatedOrStaticOutput()
    {
        var valid = Animation(2, 20);
        var wrongDelay = Animation(2, 40);
        var wrongCount = Animation(3, 20);
        var single = Animation(1, 20);
        foreach (var (bytes, width) in new[] { (wrongCount, 16), (wrongDelay, 16), (single, 16), (valid, 32), (valid[..^1], 16), (new byte[30], 16) })
        {
            var error = await Assert.ThrowsAsync<AppException>(() => ImageUploadValidator.ValidateGeneratedGifAsync(bytes, width, 16, 2, 200, default));
            Assert.Equal(MachineErrorCodes.ProviderOutputInvalid, error.MachineCode);
        }
    }

    private static byte[] Animation(int frames, int lastDelay)
    {
        using var image = new Image<Rgba32>(16, 16, new Rgba32(100, 100, 100));
        image.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 20;
        for (var i = 1; i < frames; i++)
        {
            var frame = image.Frames.AddFrame(image.Frames.RootFrame);
            frame[0, 0] = new Rgba32((byte)(100 + i), 0, 0);
            frame.Metadata.GetGifMetadata().FrameDelay = i == frames - 1 ? lastDelay : 20;
        }
        using var encoded = new MemoryStream();
        image.SaveAsGif(encoded);
        return encoded.ToArray();
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
