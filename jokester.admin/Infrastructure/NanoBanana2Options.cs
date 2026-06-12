namespace jokester.admin.Infrastructure;

public sealed class NanoBanana2Options
{
    public const string SectionName = "NanoBanana2";

    public string BaseUrl { get; init; } = "";

    public string ApiKey { get; init; } = string.Empty;

    public string ImageModel { get; init; } = "nano-banana-2";

    public string TextToImagePath { get; init; } = "/images/generations";

    public string ImageToImagePath { get; init; } = "/images/edits";
}
