using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_task")]
public sealed class AiImageTaskEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "site_id")]
    public long SiteId { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "prompt")]
    public string Prompt { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "negative_prompt")]
    public string? NegativePrompt { get; set; }

    [SugarColumn(ColumnName = "model_name")]
    public string? ModelName { get; set; }

    [SugarColumn(ColumnName = "image_count")]
    public int ImageCount { get; set; } = 1;

    [SugarColumn(ColumnName = "resolution_code")]
    public string ResolutionCode { get; set; } = "1k";

    [SugarColumn(ColumnName = "quality_code")]
    public string QualityCode { get; set; } = "med";

    [SugarColumn(ColumnName = "aspect_ratio_code")]
    public string AspectRatioCode { get; set; } = "1:1";

    [SugarColumn(ColumnName = "width")]
    public int Width { get; set; } = 1024;

    [SugarColumn(ColumnName = "height")]
    public int Height { get; set; } = 1024;

    [SugarColumn(ColumnName = "size")]
    public string Size { get; set; } = "1024x1024";

    [SugarColumn(ColumnName = "quality")]
    public string Quality { get; set; } = "medium";

    [SugarColumn(ColumnName = "reference_image_urls")]
    public string? ReferenceImageUrls { get; set; }

    [SugarColumn(ColumnName = "mask_image_url")]
    public string? MaskImageUrl { get; set; }

    [SugarColumn(ColumnName = "result_urls")]
    public string? ResultUrls { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "error_message")]
    public string? ErrorMessage { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }

    [SugarColumn(ColumnName = "updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
