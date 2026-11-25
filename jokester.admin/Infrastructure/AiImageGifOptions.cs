namespace jokester.admin.Infrastructure;

public sealed class AiImageGifOptions
{
    public const string SectionName = "AiImageGif";
    public bool AcceptNewTasks { get; set; } = true;
    public int[] FrameCounts { get; set; } = [2, 4, 6, 8, 9, 10, 12];
    public int DefaultFrameCount { get; set; } = 9;
    public int[] FrameDurationsMs { get; set; } = [100, 200, 400, 800, 1000];
    public int DefaultFrameDurationMs { get; set; } = 200;
    public GifTokenPricingOptions TokenPricing { get; set; } = new();
    public GifReserveOptions Reserve { get; set; } = new();

    internal bool IsValid() => FrameCounts.Length > 0 && FrameCounts.All(n => n is 2 or 4 or 6 or 8 or 9 or 10 or 12)
        && FrameCounts.Distinct().Count() == FrameCounts.Length && FrameCounts.Contains(DefaultFrameCount)
        && FrameDurationsMs.Length > 0 && FrameDurationsMs.All(ms => ms is 100 or 200 or 400 or 800 or 1000)
        && FrameDurationsMs.Distinct().Count() == FrameDurationsMs.Length && FrameDurationsMs.Contains(DefaultFrameDurationMs)
        && TokenPricing.IsValid() && Reserve.IsValid();
}

public sealed class GifReserveOptions
{
    public int BasePoints { get; set; } = 2;
    public Dictionary<string, int> PerFramePoints { get; set; } = new() { ["low"] = 4, ["med"] = 9, ["high"] = 12 };
    public int PerReferencePoints { get; set; } = 6;

    internal bool IsValid() => BasePoints >= 0 && PerReferencePoints >= 0 && PerFramePoints.Count == 3
        && new[] { "low", "med", "high" }.All(code => PerFramePoints.TryGetValue(code, out var points)
            && points > 0 && ((long)BasePoints + 12L * points + 6L * PerReferencePoints) * 10L <= int.MaxValue);
}

public sealed class GifTokenPricingOptions
{
    public decimal UsdToCny { get; set; } = 6.6645m;
    public decimal DiscountRate { get; set; } = 0.75m;
    public decimal CnyPerPoint { get; set; } = 0.01m;
    public GifTokenRateOptions TextInput { get; set; } = new(5m, 25m);
    public GifTokenRateOptions ImageInput { get; set; } = new(8m, 40m);
    public GifTokenRateOptions Output { get; set; } = new(30m, 150m);

    internal bool IsValid() => UsdToCny > 0 && DiscountRate is > 0 and <= 1
        && GifTokenRateOptions.ValidRate(CnyPerPoint)
        && new[] { TextInput, ImageInput, Output }.All(rate => GifTokenRateOptions.ValidRate(rate.OfficialUsdPerMillion)
            && GifTokenRateOptions.ValidRate(rate.PlatformCnyPerMillion))
        && TextInput.PlatformCnyPerMillion <= ImageInput.PlatformCnyPerMillion;
}

public sealed class GifTokenRateOptions
{
    public GifTokenRateOptions() { }
    public GifTokenRateOptions(decimal usd, decimal cny) => (OfficialUsdPerMillion, PlatformCnyPerMillion) = (usd, cny);
    public decimal OfficialUsdPerMillion { get; set; }
    public decimal PlatformCnyPerMillion { get; set; }
    internal static bool ValidRate(decimal rate) => rate is > 0 and <= 1_000_000m && decimal.Round(rate, 6) == rate;
}
