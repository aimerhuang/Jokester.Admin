using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.DTOs.NanoBananaImages;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace jokester.admin.Tests;

public sealed class AiImageGifCreationTests : IDisposable
{
    private const string Model = "gpt-image-2.6-flare";
    private readonly PointServiceBillingTests.TestContext _billing = new(500);
    private readonly HttpClient _http = new();
    private readonly AiImageGifOptions _options = new();
    private readonly Mock<IAiImageAdmissionService> _admission = new();
    private readonly Mock<IAiImageModelConfigService> _models = new();
    private readonly Mock<IMediaAssetService> _media = new();
    private readonly Mock<IAiImageTaskQueue> _queue = new();
    private readonly AiImageService _service;
    private readonly NanoBananaImageService _nano;

    public AiImageGifCreationTests()
    {
        _billing.Db.Ado.ExecuteCommand("CREATE TABLE sys_site (id INTEGER, is_deleted INTEGER); INSERT INTO sys_site VALUES (1,0);");
        var user = Mock.Of<ICurrentUser>(x => x.UserId == 1);
        var route = new ResolvedAiImageModelConfig { ModelCode = Model, ProviderProtocol = "openai-image" };
        _models.Setup(x => x.ResolveGifRoutesAsync(Model, default)).ReturnsAsync([route]);
        _queue.Setup(x => x.TryQueue(It.IsAny<long>())).Returns(true);
        _admission.Setup(x => x.ReserveAsync(1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), default))
            .ReturnsAsync((long userId, string key, string hash, int count, int cost, CancellationToken _) =>
                new AiImageAdmissionReservation(userId, key, hash, "20261125", count, cost, "test-owner", false, 0));
        _admission.Setup(x => x.AdjustPointCostAsync(It.IsAny<AiImageAdmissionReservation>(), It.IsAny<int>(), default))
            .ReturnsAsync((AiImageAdmissionReservation reservation, int points, CancellationToken _) => reservation with { PointCost = points });
        var sizeMode = Options.Create(new AiImageSizeModeOptions());
        var prompts = Options.Create(new PromptLibraryOptions());
        var paths = Mock.Of<IAiMediaPathResolver>();
        var filter = Mock.Of<IAiPromptFilter>();
        _service = new(_http, _models.Object, _billing.Service, _billing.Db, user, _queue.Object, _admission.Object,
            Options.Create(new OpenAiOptions()), sizeMode, prompts, paths, filter, _media.Object,
            Mock.Of<IAiImageCatalogService>(), Mock.Of<IAiSizeModeRolloutPolicy>(), NullLogger<AiImageService>.Instance, Options.Create(_options));
        _nano = new(_http, _models.Object, _billing.Service, _billing.Db, user, _queue.Object, _admission.Object,
            prompts, paths, filter, _media.Object, sizeMode, NullLogger<NanoBananaImageService>.Instance);
    }

    [Fact]
    public async Task ExplicitPng_ReplaysTheOriginalPngFingerprintAtItsFlatPrice()
    {
        _billing.Db.Ado.ExecuteCommand("""
            CREATE TABLE ai_image_parameter (id INTEGER PRIMARY KEY, param_type TEXT, param_code TEXT, param_name TEXT,
                provider_value TEXT, value_int_1 INTEGER, value_int_2 INTEGER, sort INTEGER, status INTEGER,
                created_at TEXT, updated_at TEXT, is_deleted INTEGER);
            INSERT INTO ai_image_parameter VALUES
              (1,'resolution','1k','1k',NULL,1024,NULL,1,1,CURRENT_TIMESTAMP,NULL,0),
              (2,'quality','med','medium','medium',NULL,NULL,1,1,CURRENT_TIMESTAMP,NULL,0),
              (3,'aspect_ratio','1:1','1:1',NULL,1,1,1,1,CURRENT_TIMESTAMP,NULL,0);
            """);
        _models.Setup(x => x.ResolveAsync("gpt-image-2", "1k", default))
            .ReturnsAsync(new ResolvedAiImageModelConfig { ModelCode = "gpt-image-2" });
        static CreateAiImageTaskRequest Png(string? format) => new()
        {
            SiteId = 1, IdempotencyKey = "legacy-png-compatibility", Prompt = "a still frame",
            ModelCode = "gpt-image-2", ResolutionCode = "1k", QualityCode = "med", AspectRatioCode = "1:1", OutputFormat = format
        };
        var original = await _service.CreateAsync(Png(null), default);
        Assert.Equal(original, await _service.CreateAsync(Png("png"), default));
        var task = _billing.Db.Queryable<AiImageTaskEntity>().Single(x => x.Id == original);
        Assert.Equal("9c0913e31681cf43b7b7ba0feac5790ac587c3caf93a095b6129eb44650ab372", task.RequestFingerprint);
        Assert.Equal("standard", task.PricingTier);
        Assert.Equal(100, task.PointCost);
        Assert.Null(task.FrameCount);
        Assert.Null(task.FrameDurationMs);
        Assert.Single(_billing.GetDetails());
    }

    [Fact]
    public async Task BatchReplay_AfterGifIsDisabled_ReturnsOriginalTasksAndConflictsOnDifferentFrames()
    {
        var first = await _service.CreateTasksResponseAsync(Request(count: 2), default);
        _options.AcceptNewTasks = false;
        var replay = await _service.CreateTasksResponseAsync(Request(count: 2), default);
        Assert.Equal(first.TaskIds, replay.TaskIds);
        Assert.Equal(2, replay.TaskIds.Distinct().Count());
        Assert.Equal("gif_fixed", replay.PricingTier);
        Assert.Equal(38, replay.UnitPointCost);
        var changed = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateTasksResponseAsync(Request(count: 2, frames: 4), default));
        Assert.Equal(ErrorCodes.Conflict, changed.Code);
        Assert.Equal(424, _billing.GetUser().PointBalance);
        Assert.Equal(2, _billing.GetDetails().Count);
        _admission.Verify(x => x.ReserveAsync(1, It.IsAny<string>(), It.IsAny<string>(), 2, 76, default), Times.Once);
        _models.Verify(x => x.ResolveGifRoutesAsync(Model, default), Times.Once);
        _media.Verify(x => x.ResolveOwnedReferenceUrlsAsync(1, false, It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<IReadOnlyList<string>>(), default), Times.Once);
        _queue.Verify(x => x.TryQueue(It.IsAny<long>()), Times.Exactly(2));
    }

    [Fact]
    public void UnsplitUsage_PreservesUnknownBreakdownFieldsInApiResponse()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.SerializeToElement(new GifUsageDto(1024, null, null, 439, "unsplit"), options);
        Assert.Equal(1024, json.GetProperty("inputTokens").GetInt32());
        Assert.Equal("unsplit", json.GetProperty("inputBreakdown").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("textInputTokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("imageInputTokens").ValueKind);
    }

    [Theory]
    [InlineData("1:1", 768, 768)]
    [InlineData("16:9", 1024, 576)]
    [InlineData("9:16", 576, 1024)]
    [InlineData("4:3", 896, 672)]
    [InlineData("3:4", 672, 896)]
    [InlineData("3:2", 960, 640)]
    [InlineData("2:3", 640, 960)]
    [InlineData("21:9", 1120, 480)]
    public async Task Create_StoresRequestedFrameGeometry(string ratio, int width, int height)
    {
        var id = await _service.CreateAsync(Request(aspect: ratio, frames: 12, duration: 800), default);
        var task = _billing.Db.Queryable<AiImageTaskEntity>().Single(x => x.Id == id);
        Assert.Equal("gif", task.OutputFormat);
        Assert.Null(task.ResolutionCode);
        Assert.Equal(12, task.FrameCount);
        Assert.Equal(800, task.FrameDurationMs);
        Assert.Equal(width, task.RequestedWidth);
        Assert.Equal(height, task.RequestedHeight);
        Assert.Equal(0, width % 16);
        Assert.Equal(0, height % 16);
        Assert.InRange((long)width * height * task.FrameCount.GetValueOrDefault(), 1, 8_294_400);
    }

    [Theory]
    [InlineData("gpt-direct")]
    [InlineData("versioned")]
    [InlineData("nano-create")]
    [InlineData("nano-direct")]
    [InlineData("png-with-frames")]
    [InlineData("frame-count")]
    [InlineData("duration")]
    [InlineData("quality")]
    [InlineData("aspect")]
    public async Task InvalidGifRequest_IsRejectedBeforeQuotaAndCharge(string variant)
    {
        var error = await Assert.ThrowsAsync<AppException>(() => variant switch
        {
            "gpt-direct" => _service.GenerateAsync(new GenerateAiImageRequest { OutputFormat = "gif" }, default),
            "versioned" => _service.CreateAsync(Request(sizeMode: "explicit"), default),
            "nano-create" => _nano.CreateAsync(new CreateNanoBananaImageTaskRequest { OutputFormat = "gif" }, default),
            "nano-direct" => _nano.GenerateAsync(new GenerateNanoBananaImageRequest { OutputFormat = "gif" }, default),
            "png-with-frames" => _service.CreateAsync(Request(format: "png", frames: 4), default),
            "frame-count" => _service.CreateAsync(Request(frames: 3), default),
            "duration" => _service.CreateAsync(Request(duration: 20), default),
            "quality" => _service.CreateAsync(Request(quality: "xhigh"), default),
            "aspect" => _service.CreateAsync(Request(aspect: "auto"), default),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        });
        Assert.Equal(ErrorCodes.BadRequest, error.Code);
        Assert.Equal(MachineErrorCodes.GifNotSupported, error.MachineCode);
        _admission.VerifyNoOtherCalls();
        _models.VerifyNoOtherCalls();
        Assert.Empty(_billing.GetDetails());
    }

    private static CreateAiImageTaskRequest Request(int count = 1, string? sizeMode = null, int? frames = null,
        int? duration = null, string aspect = "1:1", string quality = "low", string format = "gif") => new()
    {
        SiteId = 1, IdempotencyKey = "gif-playground-creation", Prompt = "a cat watching the sunset", ModelCode = Model,
        QualityCode = quality, AspectRatioCode = aspect, ImageCount = count, OutputFormat = format,
        FrameCount = frames, FrameDurationMs = duration, SizeMode = sizeMode
    };

    public void Dispose() { _http.Dispose(); _billing.Dispose(); }
}
