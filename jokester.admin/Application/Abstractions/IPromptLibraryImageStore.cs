namespace jokester.admin.Application.Abstractions;

public interface IPromptLibraryImageStore
{
    string RootPath { get; }

    string CreateStagingDirectory(long syncRunId);

    void DeleteStagingDirectory(string stagingDirectory);

    bool IsStoredImageAvailable(string? relativePath);

    Task<PromptStoredImage> PrepareAsync(
        int externalNo,
        string sourceUrl,
        string? reusableRelativePath,
        string stagingDirectory,
        CancellationToken cancellationToken);

    Task CleanupAsync(
        IReadOnlySet<string> referencedRelativePaths,
        DateTime retainAfterUtc,
        CancellationToken cancellationToken);

    long GetStoredBytes();

    long? GetAvailableBytes();
}

public sealed record PromptStoredImage(string RelativePath, bool Reused);
