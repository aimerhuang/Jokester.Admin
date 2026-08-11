using System.Text.RegularExpressions;
using Cronos;
using Microsoft.Extensions.Options;

namespace jokester.admin.Infrastructure;

public sealed partial class PromptLibraryOptionsValidator : IValidateOptions<PromptLibraryOptions>
{
    public ValidateOptionsResult Validate(string? name, PromptLibraryOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (!SourceCodeRegex().IsMatch(options.Source))
        {
            failures.Add("PromptLibrary:Source must contain only lowercase ASCII letters, digits, and hyphens.");
        }
        if (!TryValidateHttpsUrl(options.SourceApiUrl))
        {
            failures.Add("PromptLibrary:SourceApiUrl must be an absolute HTTPS URL without user information or a fragment.");
        }
        if (!string.IsNullOrEmpty(options.SourceApiToken) && string.IsNullOrWhiteSpace(options.SourceApiToken))
        {
            failures.Add("PromptLibrary:SourceApiToken cannot contain only whitespace.");
        }
        if (options.TargetCount is < 1 or > 500)
        {
            failures.Add("PromptLibrary:TargetCount must be between 1 and 500.");
        }
        try
        {
            _ = CronExpression.Parse(options.SyncCron, CronFormat.IncludeSeconds);
        }
        catch (CronFormatException)
        {
            failures.Add("PromptLibrary:SyncCron must be a valid six-field cron expression.");
        }
        if (!string.IsNullOrWhiteSpace(options.HttpProxy)
            && (!Uri.TryCreate(options.HttpProxy, UriKind.Absolute, out var proxyUri)
                || (proxyUri.Scheme != Uri.UriSchemeHttp && proxyUri.Scheme != Uri.UriSchemeHttps)))
        {
            failures.Add("PromptLibrary:HttpProxy must be an absolute HTTP(S) URL when configured.");
        }
        if (options.ConnectTimeoutSeconds is < 1 or > 120)
        {
            failures.Add("PromptLibrary:ConnectTimeoutSeconds must be between 1 and 120.");
        }
        if (options.TotalTimeoutSeconds is < 30 or > 3600)
        {
            failures.Add("PromptLibrary:TotalTimeoutSeconds must be between 30 and 3600.");
        }
        if (options.RetryCount is < 0 or > 10)
        {
            failures.Add("PromptLibrary:RetryCount must be between 0 and 10.");
        }
        ValidateImageRoot(options.ImageRoot, failures);
        if (!IsValidPublicBasePath(options.PublicBasePath))
        {
            failures.Add("PromptLibrary:PublicBasePath must be an absolute local URL path without traversal.");
        }
        if (options.ImageMaxBytes is < 1_048_576 or > 52_428_800)
        {
            failures.Add("PromptLibrary:ImageMaxBytes must be between 1MB and 50MB.");
        }
        if (options.ImageRetainDays is < 1 or > 90)
        {
            failures.Add("PromptLibrary:ImageRetainDays must be between 1 and 90.");
        }
        if (options.ImageAllowedHosts.Length == 0
            || options.ImageAllowedHosts.Any(host =>
                string.IsNullOrWhiteSpace(host)
                || Uri.CheckHostName(host) == UriHostNameType.Unknown
                || host.Contains('/')
                || host.Contains(':')))
        {
            failures.Add("PromptLibrary:ImageAllowedHosts must contain plain host names.");
        }
        if (options.KeepSnapshots is < 2 or > 20)
        {
            failures.Add("PromptLibrary:KeepSnapshots must be between 2 and 20.");
        }
        if (options.DownloadConcurrency is < 1 or > 16)
        {
            failures.Add("PromptLibrary:DownloadConcurrency must be between 1 and 16.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool TryValidateHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment);

    private static void ValidateImageRoot(string value, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            failures.Add("PromptLibrary:ImageRoot must be a configured absolute path.");
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            var root = Path.GetPathRoot(fullPath);
            if (string.Equals(
                    fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                failures.Add("PromptLibrary:ImageRoot cannot be a filesystem root.");
            }

            var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var imageRoot = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (imageRoot.StartsWith(
                    baseDirectory,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                failures.Add("PromptLibrary:ImageRoot must be outside the application publish directory.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            failures.Add("PromptLibrary:ImageRoot is not a valid filesystem path.");
        }
    }

    private static bool IsValidPublicBasePath(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith('/')
        && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.Contains("..", StringComparison.Ordinal)
        && !value.Contains('?', StringComparison.Ordinal)
        && !value.Contains('#', StringComparison.Ordinal);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceCodeRegex();

}
