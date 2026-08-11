using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("prompt_library_metric_daily")]
public sealed class PromptLibraryMetricDailyEntity
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "prompt_id")]
    public long PromptId { get; set; }

    [SugarColumn(IsPrimaryKey = true, ColumnName = "metric_date")]
    public DateTime MetricDate { get; set; }

    [SugarColumn(ColumnName = "detail_view_count")]
    public long DetailViewCount { get; set; }

    [SugarColumn(ColumnName = "copy_count")]
    public long CopyCount { get; set; }

    [SugarColumn(ColumnName = "use_count")]
    public long UseCount { get; set; }

    [SugarColumn(ColumnName = "successful_generation_count")]
    public long SuccessfulGenerationCount { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime UpdatedAt { get; set; }
}
