namespace jokester.admin.Application.DTOs.Prompts;

public sealed class PromptLibrarySyncStatusDto
{
    public bool Enabled { get; init; }

    public bool IsQueued { get; init; }

    public bool IsRunning { get; init; }

    public bool IsSwitchingSnapshot { get; init; }

    public long? CurrentSnapshotId { get; init; }

    public int ActiveItemCount { get; init; }

    public int LocalCoverCount { get; init; }

    public int MissingCoverCount { get; init; }

    public long ImageBytes { get; init; }

    public long? DiskFreeBytes { get; init; }

    public DateTime? LastSuccessfulAt { get; init; }

    public string? CurrentContentHash { get; init; }

    public int ConsecutiveFailureCount { get; init; }

    public PromptLibrarySyncRunDto? LatestRun { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class PromptLibrarySyncRunDto
{
    public long Id { get; init; }

    public string Source { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? SourceContentHash { get; init; }

    public int ParsedCount { get; init; }

    public int SelectedCount { get; init; }

    public int DownloadedCount { get; init; }

    public int ReusedImageCount { get; init; }

    public int FailedImageCount { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime? FinishedAt { get; init; }

    public string? ErrorMessage { get; init; }

    public string? WarningMessage { get; init; }
}

public sealed class QueuePromptLibrarySyncResponse
{
    public bool Accepted { get; init; }

    public bool IsQueued { get; init; }

    public bool IsRunning { get; init; }

    public bool IsSwitchingSnapshot { get; init; }
}

public sealed class SwitchPromptLibrarySnapshotResponse
{
    public long SnapshotId { get; init; }

    public long? PreviousSnapshotId { get; init; }

    public bool Changed { get; init; }

    public int ActiveItemCount { get; init; }
}
