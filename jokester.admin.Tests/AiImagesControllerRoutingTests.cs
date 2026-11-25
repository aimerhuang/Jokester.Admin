using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.DTOs.NanoBananaImages;
using jokester.admin.Application.Services;
using jokester.admin.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Moq;

namespace jokester.admin.Tests;

public sealed class AiImagesControllerRoutingTests
{
    [Fact]
    public async Task LegacyPricingKeepsArrayAndReturnsPrivateRevisionEvenWhenEmpty()
    {
        var images = new Mock<IAiImageService>();
        images.Setup(x => x.GetPricingRevisionAsync(default)).ReturnsAsync(12);
        images.Setup(x => x.GetPricingOptionsAsync(default)).ReturnsAsync(Array.Empty<AiImagePricingOptionDto>());
        var controller = new AiImagesController(images.Object, Mock.Of<INanoBananaImageService>(),
            Mock.Of<IAiImageModelConfigService>(), Mock.Of<IAiImageCatalogService>(), Mock.Of<IAiSizeModeRolloutPolicy>())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = Assert.IsType<OkObjectResult>(await controller.GetPricingOptions(null, null, default));
        var json = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(12, json.GetProperty("pricingRevision").GetInt64());
        Assert.Equal(0, json.GetProperty("data").GetArrayLength());
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Contains("Authorization", controller.Response.Headers.Vary.ToString());
    }

    [Fact]
    public async Task Create_RoutesArbitraryModelCodeByConfiguredProviderProtocol()
    {
        const string modelCode = "ios-featured-image-model";
        var request = new CreateAiImageTaskRequest
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            Prompt = "a configured image request",
            ModelCode = modelCode,
            Resolution = "2k",
            ResolutionCode = "1k",
            AspectRatioCode = "16:9",
            ImageCount = 1,
            ReferenceAssetIds = ["AST000000000001"]
        };
        var gptService = new Mock<IAiImageService>(MockBehavior.Strict);
        var geminiService = new Mock<INanoBananaImageService>(MockBehavior.Strict);
        var modelConfigService = new Mock<IAiImageModelConfigService>(MockBehavior.Strict);

        modelConfigService
            .Setup(x => x.ResolveAsync(modelCode, "2k", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedAiImageModelConfig
            {
                ModelCode = modelCode,
                Provider = AiImageModelConfigService.GeminiImageProtocol,
                ProviderProtocol = AiImageModelConfigService.GeminiImageProtocol,
                ConsentProviderCode = "google"
            });
        geminiService
            .Setup(x => x.CreateAsync(
                It.Is<CreateNanoBananaImageTaskRequest>(mapped =>
                    mapped.ModelCode == modelCode
                    && mapped.Prompt == request.Prompt
                    && mapped.ResolutionCode == "2k"
                    && mapped.ImageAssetIds.SequenceEqual(request.ReferenceAssetIds)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(321L);

        gptService.Setup(x => x.BuildCreateTasksResponseAsync(It.IsAny<IReadOnlyList<long>>(), "active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateAiImageTasksResponse { Id = 321 });

        var controller = new AiImagesController(
            gptService.Object,
            geminiService.Object,
            modelConfigService.Object,
            Mock.Of<IAiImageCatalogService>(),
            Mock.Of<IAiSizeModeRolloutPolicy>());

        await controller.Create(request, default);

        modelConfigService.VerifyAll();
        geminiService.VerifyAll();
        gptService.VerifyAll();
    }
}
