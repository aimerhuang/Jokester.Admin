namespace jokester.admin.Application.Abstractions;

/// <summary>已校验的 GIF 单帧尺寸和上游计量信息。</summary>
public sealed record GifTaskResult(int Width, int Height, GifTokenUsage? Usage);

/// <summary>输入未拆分时保留总量，两个明细同时为空。</summary>
public sealed record GifTokenUsage(int InputTokens, int? TextInputTokens, int? ImageInputTokens, int OutputTokens)
{
    public string InputBreakdown => TextInputTokens.HasValue && ImageInputTokens.HasValue ? "split" : "unsplit";
}
