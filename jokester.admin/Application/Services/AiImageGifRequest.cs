using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Infrastructure;

namespace jokester.admin.Application.Services;

internal static class AiImageGifRequest
{
    public static AppException Unsupported() =>
        new(ErrorCodes.BadRequest, MachineErrorCodes.GifNotSupported, "当前模型或接口不支持这些 GIF 参数");

    public static bool IsGif(AiImageOutputRequest request, bool allowGif)
    {
        var format = request.OutputFormat?.Trim().ToLowerInvariant() ?? "png";
        if (format == "png" && request.FrameCount is null && request.FrameDurationMs is null)
            return false;
        if (format == "gif" && allowGif)
            return true;
        throw Unsupported();
    }

    public static AiImageRequestIdentity Identify(CreateAiImageTaskRequest request, string modelCode, string prompt) =>
        AiImageIdempotency.Create(request.IdempotencyKey, new
        {
            Endpoint = "gpt-image", OutputFormat = "gif", request.SiteId, request.SourcePromptId,
            Prompt = prompt, NegativePrompt = request.NegativePrompt?.Trim(), ModelCode = modelCode,
            request.ImageCount, request.FrameCount, request.FrameDurationMs,
            QualityCode = request.QualityCode?.Trim().ToLowerInvariant(), AspectRatioCode = request.AspectRatioCode?.Trim(),
            request.ReferenceAssetIds, request.ReferenceImageUrls, request.MaskAssetId, request.MaskImageUrl
        });

    public static ResolveAiImageParametersResponse ResolveParameters(CreateAiImageTaskRequest request, AiImageGifOptions options)
    {
        if (!options.AcceptNewTasks)
            throw Unsupported();
        var quality = request.QualityCode?.Trim().ToLowerInvariant();
        var aspect = request.AspectRatioCode?.Trim();
        if (quality is not ("low" or "med" or "high")
            || !options.FrameCounts.Contains(request.FrameCount ?? options.DefaultFrameCount)
            || !options.FrameDurationsMs.Contains(request.FrameDurationMs ?? options.DefaultFrameDurationMs)
            || !string.IsNullOrWhiteSpace(request.MaskAssetId) || !string.IsNullOrWhiteSpace(request.MaskImageUrl))
            throw Unsupported();
        var (width, height) = aspect switch
        {
            "1:1" => (768, 768), "16:9" => (1024, 576), "9:16" => (576, 1024),
            "4:3" => (896, 672), "3:4" => (672, 896), "3:2" => (960, 640),
            "2:3" => (640, 960), "21:9" => (1120, 480),
            _ => throw Unsupported()
        };
        return new()
        {
            AspectRatioCode = aspect, QualityCode = quality, ProviderQuality = quality == "med" ? "medium" : quality,
            Width = width, Height = height, Size = $"{width}x{height}"
        };
    }
}
