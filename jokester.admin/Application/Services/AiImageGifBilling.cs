using jokester.admin.Application.Abstractions;
using jokester.admin.Infrastructure;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Application.Services;

internal sealed record GifRateSnapshot(decimal TextInput, decimal ImageInput, decimal Output, decimal CnyPerPoint)
{
    internal static GifRateSnapshot From(GifTokenPricingOptions pricing) => new(
        pricing.TextInput.PlatformCnyPerMillion, pricing.ImageInput.PlatformCnyPerMillion,
        pricing.Output.PlatformCnyPerMillion, pricing.CnyPerPoint);
}

public static class AiImageGifBilling
{
    public static int CalculateReservePoints(AiImageGifOptions options, string qualityCode, int frameCount, int referenceCount)
    {
        if (!options.FrameCounts.Contains(frameCount) || referenceCount is < 0 or > 6
            || !options.Reserve.PerFramePoints.TryGetValue(qualityCode, out var framePrice))
            throw new AppException(ErrorCodes.BadRequest, "GIF 画质、帧数或参考图数量无效");
        return checked(options.Reserve.BasePoints + frameCount * framePrice + referenceCount * options.Reserve.PerReferencePoints);
    }

    internal static decimal CalculatePlatformCostCny(GifRateSnapshot rates, GifTokenUsage usage) =>
        ((usage.TextInputTokens ?? usage.InputTokens) * rates.TextInput
        + (usage.ImageInputTokens ?? 0) * rates.ImageInput + usage.OutputTokens * rates.Output) / 1_000_000m;
}
