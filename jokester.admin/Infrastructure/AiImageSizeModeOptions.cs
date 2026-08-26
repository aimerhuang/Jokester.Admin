namespace jokester.admin.Infrastructure;

public sealed class AiImageSizeModeOptions
{
    public const string SectionName = "AiImageSizeMode";

    public bool Enabled { get; set; }

    public bool AutoEnabled { get; set; }

    public int AutoCohortPercent { get; set; }

    public long[] AllowedUserIds { get; set; } = [];

    public string[] AllowedPlatforms { get; set; } = ["web", "android", "ios"];

    public string[] ProviderAllowedHosts { get; set; } = [];

    public string[] ResultAllowedHosts { get; set; } = [];

    public int AutoMaxOutputBytes { get; set; } = 25 * 1024 * 1024;

    public int AutoMaxWidth { get; set; } = 8192;

    public int AutoMaxHeight { get; set; } = 8192;

    public long AutoMaxPixels { get; set; } = 33_554_432;

    public int AttemptReconcileMinutes { get; set; } = 30;
}
