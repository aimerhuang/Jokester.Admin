using Microsoft.Extensions.Options;

namespace jokester.admin.Infrastructure;

public sealed class AiMediaStorageOptionsValidator : IValidateOptions<AiMediaStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, AiMediaStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            return ValidateOptionsResult.Success;
        }

        try
        {
            if (!Path.IsPathFullyQualified(options.RootPath))
            {
                return ValidateOptionsResult.Fail("AiMediaStorage:RootPath must be an absolute path when configured.");
            }

            var fullPath = Path.GetFullPath(options.RootPath);
            var fileSystemRoot = Path.GetPathRoot(fullPath);
            if (string.Equals(
                    fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    fileSystemRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return ValidateOptionsResult.Fail("AiMediaStorage:RootPath cannot be a filesystem root.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ValidateOptionsResult.Fail("AiMediaStorage:RootPath is not a valid filesystem path.");
        }

        return ValidateOptionsResult.Success;
    }
}
