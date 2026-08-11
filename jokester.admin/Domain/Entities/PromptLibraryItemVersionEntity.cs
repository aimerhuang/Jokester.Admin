using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("prompt_library_item_version")]
public sealed class PromptLibraryItemVersionEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "snapshot_id")]
    public long SnapshotId { get; set; }

    [SugarColumn(ColumnName = "prompt_id")]
    public long PromptId { get; set; }

    [SugarColumn(ColumnName = "external_no")]
    public int ExternalNo { get; set; }

    [SugarColumn(ColumnName = "external_occurrence")]
    public int ExternalOccurrence { get; set; } = 1;

    [SugarColumn(ColumnName = "title")]
    public string Title { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "description")]
    public string Description { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "prompt_text")]
    public string PromptText { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "prompt_hash")]
    public string PromptHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "cover_source_url")]
    public string CoverSourceUrl { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "cover_local_path")]
    public string CoverLocalPath { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "author_name")]
    public string? AuthorName { get; set; }

    [SugarColumn(ColumnName = "author_url")]
    public string? AuthorUrl { get; set; }

    [SugarColumn(ColumnName = "source_url")]
    public string? SourceUrl { get; set; }

    [SugarColumn(ColumnName = "source_published_at")]
    public DateTime? SourcePublishedAt { get; set; }

    [SugarColumn(ColumnName = "language")]
    public string? Language { get; set; }

    [SugarColumn(ColumnName = "source_position")]
    public int SourcePosition { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
