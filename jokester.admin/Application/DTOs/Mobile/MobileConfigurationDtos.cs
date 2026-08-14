namespace jokester.admin.Application.DTOs.Mobile;

public sealed class MobileConfigurationDto
{
    public string MinimumSupportedVersion { get; init; } = string.Empty;

    public string LatestVersion { get; init; } = string.Empty;

    public bool MaintenanceMode { get; init; }

    public MobileFeaturesDto Features { get; init; } = new();

    public MobileLegalDocumentVersionsDto LegalDocumentVersions { get; init; } = new();
}

public sealed class MobileFeaturesDto
{
    public bool AppleIap { get; init; }

    public bool AccountDeletion { get; init; }

    public bool PromptLibrary { get; init; }
}

public sealed class MobileLegalDocumentVersionsDto
{
    public string Privacy { get; init; } = string.Empty;

    public string Terms { get; init; } = string.Empty;

    public string AiProcessing { get; init; } = string.Empty;
}
