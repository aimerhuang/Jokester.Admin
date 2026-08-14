using jokester.admin.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace jokester.admin.Infrastructure;

public sealed class AiMediaPathResolver : IAiMediaPathResolver
{
    private const string RepositoryMarkerFileName = "jokester.slnx";
    private readonly string _rootWithSeparator;

    public AiMediaPathResolver(
        IOptions<AiMediaStorageOptions> options,
        IWebHostEnvironment environment,
        ILogger<AiMediaPathResolver> logger)
    {
        RootPath = ResolveRootPath(options.Value.RootPath, environment.ContentRootPath);
        _rootWithSeparator = RootPath + Path.DirectorySeparatorChar;
        logger.LogInformation("AI private media root resolved to {RootPath}", RootPath);
    }

    public string RootPath { get; }

    public string ResolveFilePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("AI media relative path cannot be empty.");
        }

        try
        {
            var normalizedPath = relativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedPath))
            {
                throw new InvalidOperationException("AI media path must be relative to its storage root.");
            }
            if (normalizedPath
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
            {
                throw new InvalidOperationException("AI media path cannot contain dot segments.");
            }

            var fullPath = Path.GetFullPath(Path.Combine(RootPath, normalizedPath));
            if (!fullPath.StartsWith(_rootWithSeparator, PathComparison))
            {
                throw new InvalidOperationException("AI media path escapes its storage root.");
            }

            return fullPath;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException("AI media path is invalid.", ex);
        }
    }

    private static string ResolveRootPath(string? configuredRootPath, string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredRootPath))
        {
            if (!Path.IsPathFullyQualified(configuredRootPath))
            {
                throw new InvalidOperationException("AiMediaStorage:RootPath must be an absolute path when configured.");
            }

            var fullConfiguredPath = Path.GetFullPath(configuredRootPath);
            var fileSystemRoot = Path.GetPathRoot(fullConfiguredPath);
            if (string.Equals(
                    TrimEndingSeparators(fullConfiguredPath),
                    fileSystemRoot is null ? null : TrimEndingSeparators(fileSystemRoot),
                    PathComparison))
            {
                throw new InvalidOperationException("AiMediaStorage:RootPath cannot be a filesystem root.");
            }

            return TrimEndingSeparators(fullConfiguredPath);
        }

        var contentRoot = Path.GetFullPath(contentRootPath);
        var repositoryRoot = FindRepositoryRoot(contentRoot);
        return TrimEndingSeparators(Path.GetFullPath(Path.Combine(
            repositoryRoot ?? contentRoot,
            "private-media",
            "ai")));
    }

    private static string? FindRepositoryRoot(string contentRootPath)
    {
        for (var directory = new DirectoryInfo(contentRootPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositoryMarkerFileName)))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string TrimEndingSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
