namespace jokester.admin.Infrastructure;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public int PrimaryTimeoutSeconds { get; init; } = 120;
}
