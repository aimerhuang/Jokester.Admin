namespace jokester.admin.Infrastructure;

public sealed class AiPromptFilterOptions
{
    public const string SectionName = "AiPromptFilter";

    public bool Enabled { get; set; } = true;

    public int RefreshIntervalSeconds { get; set; } = 30;

    public int MaxSnapshotAgeMinutes { get; set; } = 15;

    public int MinimumActiveWordCount { get; set; } = 1;
}
