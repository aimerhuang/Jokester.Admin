using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_site")]
public sealed class SysSiteEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "site_name")]
    public string SiteName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "site_code")]
    public string SiteCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "domain")]
    public string? Domain { get; set; }

    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "sort")]
    public int Sort { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
