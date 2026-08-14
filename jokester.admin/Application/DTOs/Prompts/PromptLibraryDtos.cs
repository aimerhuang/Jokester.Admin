namespace jokester.admin.Application.DTOs.Prompts;

public sealed class PromptLibraryQuery
{
    public int PageIndex { get; init; } = 1;

    public int PageSize { get; init; } = 24;

    public string? Keyword { get; init; }

    public string SearchField { get; init; } = "all";
}

public sealed class PromptLibraryListItemDto
{
    public string ContentSource { get; init; } = "curated";

    public long Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string PromptPreview { get; init; } = string.Empty;

    public string? CoverImageUrl { get; init; }

    public int SourcePosition { get; init; }
}

public sealed class PromptLibraryDetailDto
{
    public string ContentSource { get; init; } = "curated";

    public long Id { get; init; }

    public string Source { get; init; } = string.Empty;

    public int ExternalNo { get; init; }

    public int ExternalOccurrence { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string PromptText { get; init; } = string.Empty;

    public string? CoverImageUrl { get; init; }

    public string? AuthorName { get; init; }

    public string? AuthorUrl { get; init; }

    public string? SourceUrl { get; init; }

    public DateTime? SourcePublishedAt { get; init; }

    public string? Language { get; init; }

    public int SourcePosition { get; init; }
}

public sealed class RecordPromptEventRequest
{
    public string Type { get; init; } = string.Empty;
}

public sealed class RecordPromptEventResponse
{
    public string Type { get; init; } = string.Empty;

    public bool Recorded { get; init; }
}
