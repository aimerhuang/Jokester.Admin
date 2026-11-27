using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("site_community_setting")]
public sealed class SiteCommunitySettingEntity
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public int Id { get; set; }
    [SugarColumn(ColumnName = "qq_popup_enabled")]
    public bool Enabled { get; set; }
    [SugarColumn(ColumnName = "qq_group_name")]
    public string GroupName { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "qq_group_number")]
    public string GroupNumber { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "qq_join_url")]
    public string? JoinUrl { get; set; }
    [SugarColumn(ColumnName = "popup_title")]
    public string Title { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "popup_description")]
    public string Description { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "remind_interval_days")]
    public int RemindIntervalDays { get; set; }
    [SugarColumn(ColumnName = "prompt_version")]
    public int PromptVersion { get; set; }
    [SugarColumn(ColumnName = "revision")]
    public int Revision { get; set; }
    [SugarColumn(ColumnName = "updated_by")]
    public long? UpdatedBy { get; set; }
    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
