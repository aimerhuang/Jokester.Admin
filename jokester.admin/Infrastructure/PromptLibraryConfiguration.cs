using System.Globalization;

namespace jokester.admin.Infrastructure;

internal static class PromptLibraryConfiguration
{
    public static void ApplyFlatEnvironmentVariables(PromptLibraryOptions options)
    {
        Apply("PROMPT_LIBRARY_ENABLED", value => options.Enabled = ParseBoolean(value));
        Apply("PROMPT_SOURCE_API_URL", value => options.SourceApiUrl = value);
        Apply("PROMPT_SOURCE_API_TOKEN", value => options.SourceApiToken = NullIfEmpty(value));
        Apply("PROMPT_TARGET_COUNT", value => options.TargetCount = ParseInt(value));
        Apply("PROMPT_SYNC_CRON", value => options.SyncCron = value);
        Apply("PROMPT_SYNC_HTTP_PROXY", value => options.HttpProxy = NullIfEmpty(value));
        Apply("PROMPT_SYNC_CONNECT_TIMEOUT_SECONDS", value => options.ConnectTimeoutSeconds = ParseInt(value));
        Apply("PROMPT_SYNC_TOTAL_TIMEOUT_SECONDS", value => options.TotalTimeoutSeconds = ParseInt(value));
        Apply("PROMPT_SYNC_RETRY_COUNT", value => options.RetryCount = ParseInt(value));
        Apply("PROMPT_IMAGE_ROOT", value => options.ImageRoot = value);
        Apply("PROMPT_IMAGE_PUBLIC_BASE", value => options.PublicBasePath = value);
        Apply("PROMPT_IMAGE_MAX_BYTES", value => options.ImageMaxBytes = ParseLong(value));
        Apply("PROMPT_IMAGE_RETAIN_DAYS", value => options.ImageRetainDays = ParseInt(value));
        Apply("PROMPT_IMAGE_ALLOWED_HOSTS", value => options.ImageAllowedHosts = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Apply("PROMPT_KEEP_SNAPSHOTS", value => options.KeepSnapshots = ParseInt(value));
        Apply("PROMPT_DOWNLOAD_CONCURRENCY", value => options.DownloadConcurrency = ParseInt(value));
    }

    private static void Apply(string name, Action<string> setter)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (value is not null)
        {
            setter(value.Trim());
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int ParseInt(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static long ParseLong(string value) => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static bool ParseBoolean(string value) => bool.Parse(value);
}
