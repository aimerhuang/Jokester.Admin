using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("prompt_library_sync_run")]
public sealed class PromptLibrarySyncRunEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "source")]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "source_readme_hash")]
    public string? SourceContentHash { get; set; }

    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "parsed_count")]
    public int ParsedCount { get; set; }

    [SugarColumn(ColumnName = "selected_count")]
    public int SelectedCount { get; set; }

    [SugarColumn(ColumnName = "downloaded_count")]
    public int DownloadedCount { get; set; }

    [SugarColumn(ColumnName = "reused_image_count")]
    public int ReusedImageCount { get; set; }

    [SugarColumn(ColumnName = "failed_image_count")]
    public int FailedImageCount { get; set; }

    [SugarColumn(ColumnName = "started_at")]
    public DateTime StartedAt { get; set; }

    [SugarColumn(ColumnName = "finished_at")]
    public DateTime? FinishedAt { get; set; }

    [SugarColumn(ColumnName = "error_message")]
    public string? ErrorMessage { get; set; }

    [SugarColumn(ColumnName = "warning_message")]
    public string? WarningMessage { get; set; }
}
