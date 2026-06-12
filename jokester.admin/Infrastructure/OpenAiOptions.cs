namespace jokester.admin.Infrastructure;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    public string ApiKey { get; init; } = string.Empty;

    public string ImageModel { get; init; } = "gpt-image-2";
}
