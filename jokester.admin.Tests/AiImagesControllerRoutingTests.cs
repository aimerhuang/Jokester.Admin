using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.DTOs.NanoBananaImages;
using jokester.admin.Application.Services;
using jokester.admin.Controllers;
using Moq;

namespace jokester.admin.Tests;

public sealed class AiImagesControllerRoutingTests
{
    [Fact]
    public async Task Create_RoutesArbitraryModelCodeByConfiguredProviderProtocol()
    {
        const string modelCode = "ios-featured-image-model";
        var request = new CreateAiImageTaskRequest
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            Prompt = "a configured image request",
            ModelCode = modelCode,
            ResolutionCode = "2k",
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
                    && mapped.ImageAssetIds.SequenceEqual(request.ReferenceAssetIds)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(321L);

        var controller = new AiImagesController(
            gptService.Object,
            geminiService.Object,
            modelConfigService.Object);

        await controller.Create(request, default);

        modelConfigService.VerifyAll();
        geminiService.VerifyAll();
        gptService.VerifyNoOtherCalls();
    }
}
