using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_model_config")]
public sealed class AiImageModelConfigEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "model_code")]
    public string ModelCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "model_name")]
    public string ModelName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "provider")]
    public string Provider { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "provider_model")]
    public string ProviderModel { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "resolution_code")]
    public string? ResolutionCode { get; set; }

    [SugarColumn(ColumnName = "base_url")]
    public string BaseUrl { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "text_to_image_path")]
    public string TextToImagePath { get; set; } = "/images/generations";

    [SugarColumn(ColumnName = "image_to_image_path")]
    public string ImageToImagePath { get; set; } = "/images/edits";

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
