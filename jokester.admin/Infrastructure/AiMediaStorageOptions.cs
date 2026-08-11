namespace jokester.admin.Infrastructure;

public sealed class AiMediaStorageOptions
{
    public const string SectionName = "AiMediaStorage";

    public string RootPath { get; set; } = string.Empty;
}
