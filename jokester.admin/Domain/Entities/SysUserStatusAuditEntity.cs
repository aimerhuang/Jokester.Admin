using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("sys_user_status_audit")]
public sealed class SysUserStatusAuditEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(ColumnName = "user_id")] public long UserId { get; set; }
    [SugarColumn(ColumnName = "action")] public string Action { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "reason", IsNullable = true)] public string? Reason { get; set; }
    [SugarColumn(ColumnName = "operator_user_id")] public long OperatorUserId { get; set; }
    [SugarColumn(ColumnName = "operator_ip", IsNullable = true)] public string? OperatorIp { get; set; }
    [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
}
