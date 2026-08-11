namespace jokester.admin.Application.Models.PromptLibrary;

public sealed record PromptReadmeParseOptions(IReadOnlyCollection<string> AllowedImageHosts);

public sealed record PromptReadmeParseResult(
    IReadOnlyList<ParsedPromptReadmeItem> Items,
    IReadOnlyList<SkippedPromptReadmeItem> SkippedItems,
    IReadOnlyList<PromptReadmeDiagnostic> Diagnostics)
{
    public int CandidateCount => Items.Count + SkippedItems.Count;
}

public sealed record ParsedPromptReadmeItem(
    int ExternalNo,
    string? StableId,
    string Title,
    string Description,
    string PromptText,
    string CoverSourceUrl,
    string? AuthorName,
    string? AuthorUrl,
    string? SourceUrl,
    string? Published,
    string? Language,
    int SourcePosition,
    PromptReadmeSourceSpan SourceSpan);

public sealed record SkippedPromptReadmeItem(
    int SourcePosition,
    int? ExternalNo,
    string Heading,
    PromptReadmeSourceSpan SourceSpan,
    IReadOnlyList<PromptReadmeDiagnostic> Diagnostics);

public sealed record PromptReadmeDiagnostic(
    string Code,
    string Message,
    PromptReadmeDiagnosticSeverity Severity,
    int? SourcePosition,
    PromptReadmeSourceSpan? SourceSpan);

public sealed record PromptReadmeSourceSpan(
    int Start,
    int End,
    int StartLine,
    int StartColumn);

public enum PromptReadmeDiagnosticSeverity
{
    Warning,
    Error
}

public static class PromptReadmeDiagnosticCodes
{
    public const string InvalidHeading = "invalid_heading";
    public const string MissingTitle = "missing_title";
    public const string MissingDescription = "missing_description";
    public const string DescriptionFallback = "description_fallback";
    public const string MissingPrompt = "missing_prompt";
    public const string MissingGeneratedImage = "missing_generated_image";
    public const string DisallowedImageUrl = "disallowed_image_url";
    public const string InvalidOptionalUrl = "invalid_optional_url";
    public const string DuplicateSection = "duplicate_section";
    public const string DuplicateField = "duplicate_field";
}
