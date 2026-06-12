using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_operation_log")]
public sealed class SysOperationLogEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long? UserId { get; set; }

    [SugarColumn(ColumnName = "module_name")]
    public string? ModuleName { get; set; }

    [SugarColumn(ColumnName = "action_name")]
    public string? ActionName { get; set; }

    [SugarColumn(ColumnName = "request_method")]
    public string? RequestMethod { get; set; }

    [SugarColumn(ColumnName = "request_url")]
    public string? RequestUrl { get; set; }

    [SugarColumn(ColumnName = "request_data")]
    public string? RequestData { get; set; }

    [SugarColumn(ColumnName = "response_data")]
    public string? ResponseData { get; set; }

    [SugarColumn(ColumnName = "ip")]
    public string? Ip { get; set; }

    [SugarColumn(ColumnName = "execution_ms")]
    public int? ExecutionMs { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
