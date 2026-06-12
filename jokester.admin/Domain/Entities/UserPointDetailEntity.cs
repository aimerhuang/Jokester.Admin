using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_user_point_detail")]
public sealed class UserPointDetailEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "change_points")]
    public int ChangePoints { get; set; }

    [SugarColumn(ColumnName = "balance_after")]
    public int BalanceAfter { get; set; }

    [SugarColumn(ColumnName = "change_type")]
    public string ChangeType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "source")]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "remark")]
    public string? Remark { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
