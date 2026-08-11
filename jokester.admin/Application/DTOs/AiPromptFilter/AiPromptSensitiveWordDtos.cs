using jokester.admin.Application.DTOs.Common;

namespace jokester.admin.Application.DTOs.AiPromptFilter;

public sealed class AiPromptSensitiveWordQuery : PageQuery
{
    public string? Keyword { get; init; }

    public string? LanguageCode { get; init; }

    public string? CategoryCode { get; init; }

    public string? MatchMode { get; init; }

    public string? Action { get; init; }

    public int? Status { get; init; }
}

public sealed class AiPromptSensitiveWordDto
{
    public long Id { get; init; }

    public string Term { get; init; } = string.Empty;

    public string LanguageCode { get; init; } = string.Empty;

    public string CategoryCode { get; init; } = string.Empty;

    public string MatchMode { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public int Severity { get; init; }

    public int Status { get; init; }

    public string? SourceCode { get; init; }

    public string? SourceVersion { get; init; }

    public string? Remark { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }
}

public sealed class SaveAiPromptSensitiveWordRequest
{
    public string Term { get; init; } = string.Empty;

    public string LanguageCode { get; init; } = string.Empty;

    public string CategoryCode { get; init; } = string.Empty;

    public string MatchMode { get; init; } = string.Empty;

    public string Action { get; init; } = "block";

    public int Severity { get; init; } = 1;

    public int Status { get; init; } = 1;

    public string? SourceCode { get; init; }

    public string? SourceVersion { get; init; }

    public string? Remark { get; init; }
}

public sealed class UpdateAiPromptSensitiveWordStatusRequest
{
    public int Status { get; init; }
}

public sealed class TestAiPromptFilterRequest
{
    public string Text { get; init; } = string.Empty;
}

public sealed class TestAiPromptFilterResponse
{
    public bool IsAllowed { get; init; }

    public long Revision { get; init; }

    public long? RuleId { get; init; }

    public string? LanguageCode { get; init; }

    public string? CategoryCode { get; init; }

    public string? MatchMode { get; init; }

    public string? Action { get; init; }

    public int? Severity { get; init; }
}

public sealed class AiPromptFilterStatusDto
{
    public long Revision { get; init; }

    public int ActiveRuleCount { get; init; }
}
