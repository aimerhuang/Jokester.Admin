using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_parameter")]
public sealed class AiImageParameterEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "param_type")]
    public string ParamType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "param_code")]
    public string ParamCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "param_name")]
    public string ParamName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "provider_value")]
    public string? ProviderValue { get; set; }

    [SugarColumn(ColumnName = "value_int_1")]
    public int? ValueInt1 { get; set; }

    [SugarColumn(ColumnName = "value_int_2")]
    public int? ValueInt2 { get; set; }

    [SugarColumn(ColumnName = "sort")]
    public int Sort { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; } = 1;

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
