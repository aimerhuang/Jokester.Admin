namespace jokester.admin.Infrastructure;

public sealed class MobileConfigurationOptions
{
    public const string SectionName = "Mobile";

    public string MinimumSupportedVersion { get; set; } = "0.0.0";

    public string LatestVersion { get; set; } = "0.0.0";

    public bool MaintenanceMode { get; set; }

    public MobileFeatureOptions Features { get; set; } = new();
}

public sealed class MobileFeatureOptions
{
    public bool AppleIap { get; set; } = true;

    public bool AccountDeletion { get; set; } = true;

    public bool PromptLibrary { get; set; } = true;
}
