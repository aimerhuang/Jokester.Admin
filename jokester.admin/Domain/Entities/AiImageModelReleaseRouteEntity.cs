using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("ai_image_model_release_route")]
public sealed class AiImageModelReleaseRouteEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "model_release_id")]
    public long ModelReleaseId { get; set; }

    [SugarColumn(ColumnName = "route_config_id")]
    public long RouteConfigId { get; set; }

    [SugarColumn(ColumnName = "size_mode")]
    public string SizeMode { get; set; } = "explicit";

    [SugarColumn(ColumnName = "resolution_code")]
    public string ResolutionCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "route_role")]
    public string RouteRole { get; set; } = "primary";

    [SugarColumn(ColumnName = "provider_protocol")]
    public string ProviderProtocol { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "consent_provider_code")]
    public string ConsentProviderCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "provider_model")]
    public string ProviderModel { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "base_url")]
    public string BaseUrl { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "text_to_image_path")]
    public string TextToImagePath { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "image_to_image_path")]
    public string ImageToImagePath { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "secret_version_hash")]
    public string SecretVersionHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "verified_generations")]
    public bool VerifiedGenerations { get; set; }

    [SugarColumn(ColumnName = "verified_edits")]
    public bool VerifiedEdits { get; set; }

    [SugarColumn(ColumnName = "verified_mask_edits")]
    public bool VerifiedMaskEdits { get; set; }

    [SugarColumn(ColumnName = "sort")]
    public int Sort { get; set; }
}
