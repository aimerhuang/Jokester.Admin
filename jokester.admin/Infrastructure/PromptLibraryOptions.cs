namespace jokester.admin.Infrastructure;

public sealed class PromptLibraryOptions
{
    public const string SectionName = "PromptLibrary";
    public const string DefaultSource = "youmind-gpt-image-2";
    public const string DefaultSourceUrl =
        "https://raw.githubusercontent.com/YouMind-OpenLab/awesome-gpt-image-2/589f148fd605574569580665403311c5eb88143e/README_zh.md";

    public bool Enabled { get; set; }

    public string Source { get; set; } = DefaultSource;

    public string SourceApiUrl { get; set; } = DefaultSourceUrl;

    public string? SourceApiToken { get; set; }

    public int TargetCount { get; set; } = 126;

    public string SyncCron { get; set; } = "0 30 2,14 * * *";

    public string? HttpProxy { get; set; }

    public int ConnectTimeoutSeconds { get; set; } = 10;

    public int TotalTimeoutSeconds { get; set; } = 600;

    public int RetryCount { get; set; } = 3;

    public string ImageRoot { get; set; } = string.Empty;

    public string PublicBasePath { get; set; } = "/prompt-images";

    public long ImageMaxBytes { get; set; } = 15 * 1024 * 1024;

    public int ImageRetainDays { get; set; } = 7;

    public string[] ImageAllowedHosts { get; set; } =
    [
        "cms-assets.youmind.com",
        "marketing-assets.youmind.com"
    ];

    public int KeepSnapshots { get; set; } = 2;

    public int DownloadConcurrency { get; set; } = 4;
}
