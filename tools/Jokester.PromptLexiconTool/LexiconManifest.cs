namespace Jokester.PromptLexiconTool;

public sealed class LexiconManifest
{
    public string SourceCode { get; set; } = string.Empty;

    public string SourceVersion { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public string License { get; set; } = string.Empty;

    public List<LexiconFileSpec> Files { get; set; } = [];
}

public sealed class LexiconFileSpec
{
    public string Path { get; set; } = string.Empty;

    public string Format { get; set; } = "lines";

    public string LanguageCode { get; set; } = "zh";

    public string CategoryCode { get; set; } = string.Empty;

    public string MatchMode { get; set; } = "compact";

    public string ProposedAction { get; set; } = "audit";

    public int Severity { get; set; } = 3;

    public string ExpectedSha256 { get; set; } = string.Empty;

    public Dictionary<string, string> TagMappings { get; set; } = [];

    public List<string> TagPriority { get; set; } = [];
}

public sealed record PreparedLexiconResult(
    IReadOnlyList<PreparedLexiconCandidate> Candidates,
    LexiconPreparationReport Report);

public sealed record PreparedLexiconCandidate(
    string Term,
    string NormalizedTerm,
    string TermKey,
    string LanguageCode,
    string CategoryCode,
    string MatchMode,
    string ProposedAction,
    int Severity,
    int Status,
    string SourceCode,
    string SourceVersion,
    string SourceUrl,
    string License,
    string SourceFile,
    string ReviewReason);

public sealed class LexiconPreparationReport
{
    public string SourceCode { get; init; } = string.Empty;

    public string SourceVersion { get; init; } = string.Empty;

    public string SourceUrl { get; init; } = string.Empty;

    public string License { get; init; } = string.Empty;

    public DateTime GeneratedAtUtc { get; init; }

    public int RawTermCount { get; set; }

    public int CandidateCount { get; set; }

    public int DuplicateCount { get; set; }

    public int InvalidTermCount { get; set; }

    public int UnmappedTaggedTermCount { get; set; }

    public int ShortTermCount { get; set; }

    public int UrlLikeTermCount { get; set; }

    public int SpreadsheetFormulaTermCount { get; set; }

    public int CategoryConflictCount { get; set; }

    public List<LexiconInputFileReport> Files { get; init; } = [];
}

public sealed record LexiconInputFileReport(
    string Path,
    string Format,
    long SizeBytes,
    string Sha256,
    int RawTermCount,
    int AcceptedTermCount,
    int InvalidTermCount,
    int UnmappedTaggedTermCount);
