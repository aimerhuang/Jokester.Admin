namespace jokester.admin.Application.Models.AiPromptFilter;

public static class AiPromptFilterMatchModes
{
    public const string Contains = "contains";
    public const string Word = "word";
    public const string Compact = "compact";

    public static bool IsSupported(string value) => value is Contains or Word or Compact;
}

public static class AiPromptFilterActions
{
    public const string Block = "block";
    public const string Audit = "audit";

    public static bool IsSupported(string value) => value is Block or Audit;
}

public sealed record AiPromptFilterRule(
    long Id,
    string Term,
    string NormalizedTerm,
    string LanguageCode,
    string CategoryCode,
    string MatchMode,
    string Action,
    int Severity);

public sealed record AiPromptFilterMatch(
    long RuleId,
    string Term,
    string LanguageCode,
    string CategoryCode,
    string MatchMode,
    string Action,
    int Severity);

public sealed record AiPromptFilterResult(
    bool IsAllowed,
    long Revision,
    AiPromptFilterMatch? Match);

public sealed record AiPromptFilterText(string FieldName, string? Text);
