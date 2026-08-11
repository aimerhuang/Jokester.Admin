namespace jokester.admin.Application.Models.PromptLibrary;

public sealed record PromptLibrarySourceSnapshot(
    IReadOnlyList<PromptLibrarySourceItem> Items,
    int CandidateCount,
    int SkippedCount,
    string ContentHash,
    IReadOnlyList<string> Diagnostics);

public sealed record PromptLibrarySourceItem(
    string StableId,
    int ExternalNo,
    string Title,
    string Description,
    string PromptText,
    string CoverSourceUrl,
    string? AuthorName,
    string? AuthorUrl,
    string? SourceUrl,
    string? Published,
    string? Language,
    int SourcePosition);
