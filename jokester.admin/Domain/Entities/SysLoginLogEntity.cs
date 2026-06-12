using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_login_log")]
public sealed class SysLoginLogEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long? UserId { get; set; }

    [SugarColumn(ColumnName = "user_name")]
    public string? UserName { get; set; }

    [SugarColumn(ColumnName = "ip")]
    public string? Ip { get; set; }

    [SugarColumn(ColumnName = "user_agent")]
    public string? UserAgent { get; set; }

    [SugarColumn(ColumnName = "login_status")]
    public int LoginStatus { get; set; }

    [SugarColumn(ColumnName = "error_message")]
    public string? ErrorMessage { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
