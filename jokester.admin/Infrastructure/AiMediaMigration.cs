using System.Security.Cryptography;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Infrastructure;

public static class AiMediaMigration
{
    private const string PrivateMediaPrefix = "/api/media/ai/";

    public static async Task<AiMediaMigrationResult> RunAsync(
        ISqlSugarClient db,
        string contentRootPath,
        IAiMediaPathResolver mediaPathResolver,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var sourceWebRoot = Path.GetFullPath(Path.Combine(contentRootPath, "wwwroot"));
        var tasks = await db.Queryable<AiImageTaskEntity>()
            .Select(x => new MigrationTask
            {
                Id = x.Id,
                UserId = x.UserId,
                ResultUrls = x.ResultUrls,
                ReferenceImageUrls = x.ReferenceImageUrls,
                MaskImageUrl = x.MaskImageUrl
            })
            .ToListAsync(cancellationToken);

        var taskPlans = new List<TaskMigrationPlan>();
        var filePlans = new Dictionary<string, FileMigrationPlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
        {
            var resultUrls = RewriteUrlCollection(task.ResultUrls, task.UserId, sourceWebRoot, mediaPathResolver, filePlans);
            var referenceImageUrls = RewriteUrlCollection(task.ReferenceImageUrls, task.UserId, sourceWebRoot, mediaPathResolver, filePlans);
            var maskImageUrl = RewriteUrl(task.MaskImageUrl, task.UserId, sourceWebRoot, mediaPathResolver, filePlans);
            if (!resultUrls.Changed && !referenceImageUrls.Changed && !maskImageUrl.Changed)
            {
                continue;
            }

            taskPlans.Add(new TaskMigrationPlan(
                task.Id,
                resultUrls.Value,
                referenceImageUrls.Value,
                maskImageUrl.Value));
        }

        var favoritePlans = new List<FavoriteMigrationPlan>();
        if (db.DbMaintenance.IsAnyTable("ai_image_favorite", false))
        {
            var taskOwners = tasks.ToDictionary(x => x.Id, x => x.UserId);
            var favorites = await db.Queryable<AiImageFavoriteEntity>()
                .Select(x => new MigrationFavorite { Id = x.Id, TaskId = x.TaskId, ImageUrl = x.ImageUrl })
                .ToListAsync(cancellationToken);
            foreach (var favorite in favorites)
            {
                if (!taskOwners.TryGetValue(favorite.TaskId, out var ownerUserId))
                {
                    continue;
                }

                var imageUrl = RewriteUrl(favorite.ImageUrl, ownerUserId, sourceWebRoot, mediaPathResolver, filePlans);
                if (imageUrl.Changed)
                {
                    favoritePlans.Add(new FavoriteMigrationPlan(favorite.Id, imageUrl.Value!));
                }
            }
        }

        var missingFiles = filePlans.Values
            .Where(x => !File.Exists(x.SourcePath))
            .Select(x => x.SourcePath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingFiles.Length > 0)
        {
            throw new InvalidOperationException(
                $"AI media migration stopped because {missingFiles.Length} referenced source file(s) are missing:{Environment.NewLine}"
                + string.Join(Environment.NewLine, missingFiles));
        }

        var legacyFiles = EnumerateLegacyFiles(sourceWebRoot).ToArray();
        var referencedSources = filePlans.Values
            .Select(x => x.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphanFileCount = legacyFiles.Count(x => !referencedSources.Contains(x));

        if (dryRun || filePlans.Count == 0)
        {
            return new AiMediaMigrationResult(taskPlans.Count, favoritePlans.Count, filePlans.Count, orphanFileCount, dryRun);
        }

        var createdFiles = new List<string>();
        try
        {
            foreach (var filePlan in filePlans.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(filePlan.DestinationPath)!);
                if (File.Exists(filePlan.DestinationPath))
                {
                    if (!FilesMatch(filePlan.SourcePath, filePlan.DestinationPath))
                    {
                        throw new InvalidOperationException($"Destination file already exists with different content: {filePlan.DestinationPath}");
                    }
                    continue;
                }

                File.Copy(filePlan.SourcePath, filePlan.DestinationPath, overwrite: false);
                createdFiles.Add(filePlan.DestinationPath);
            }

            db.Ado.BeginTran();
            try
            {
                foreach (var taskPlan in taskPlans)
                {
                    var affected = await db.Updateable<AiImageTaskEntity>()
                        .SetColumns(x => x.ResultUrls == taskPlan.ResultUrls)
                        .SetColumns(x => x.ReferenceImageUrls == taskPlan.ReferenceImageUrls)
                        .SetColumns(x => x.MaskImageUrl == taskPlan.MaskImageUrl)
                        .Where(x => x.Id == taskPlan.Id)
                        .ExecuteCommandAsync(cancellationToken);
                    if (affected != 1)
                    {
                        throw new InvalidOperationException($"Task {taskPlan.Id} was not updated exactly once.");
                    }
                }

                foreach (var favoritePlan in favoritePlans)
                {
                    var affected = await db.Updateable<AiImageFavoriteEntity>()
                        .SetColumns(x => x.ImageUrl == favoritePlan.ImageUrl)
                        .Where(x => x.Id == favoritePlan.Id)
                        .ExecuteCommandAsync(cancellationToken);
                    if (affected != 1)
                    {
                        throw new InvalidOperationException($"Favorite {favoritePlan.Id} was not updated exactly once.");
                    }
                }

                db.Ado.CommitTran();
            }
            catch
            {
                db.Ado.RollbackTran();
                throw;
            }
        }
        catch
        {
            foreach (var createdFile in createdFiles)
            {
                File.Delete(createdFile);
            }
            throw;
        }

        return new AiMediaMigrationResult(taskPlans.Count, favoritePlans.Count, filePlans.Count, orphanFileCount, DryRun: false);
    }

    private static UrlRewriteResult RewriteUrlCollection(
        string? rawValue,
        long ownerUserId,
        string sourceWebRoot,
        IAiMediaPathResolver mediaPathResolver,
        IDictionary<string, FileMigrationPlan> filePlans)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return new UrlRewriteResult(rawValue, Changed: false);
        }

        var urls = ParseUrlCollection(rawValue);
        if (urls.Count == 0)
        {
            return new UrlRewriteResult(rawValue, Changed: false);
        }

        var changed = false;
        var rewritten = new string[urls.Count];
        for (var index = 0; index < urls.Count; index++)
        {
            var result = RewriteUrl(urls[index], ownerUserId, sourceWebRoot, mediaPathResolver, filePlans);
            rewritten[index] = result.Value ?? urls[index];
            changed |= result.Changed;
        }

        return changed
            ? new UrlRewriteResult(JsonSerializer.Serialize(rewritten), Changed: true)
            : new UrlRewriteResult(rawValue, Changed: false);
    }

    private static UrlRewriteResult RewriteUrl(
        string? rawUrl,
        long ownerUserId,
        string sourceWebRoot,
        IAiMediaPathResolver mediaPathResolver,
        IDictionary<string, FileMigrationPlan> filePlans)
    {
        if (!TryResolveLegacyPath(rawUrl, out var legacyDirectory, out var relativePath))
        {
            return new UrlRewriteResult(rawUrl, Changed: false);
        }

        var sourceDirectory = Path.GetFullPath(Path.Combine(sourceWebRoot, legacyDirectory));
        var sourcePath = ResolveContainedPath(sourceDirectory, relativePath);
        var ownerRelativePath = legacyDirectory.Equals("ai-images", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(ownerUserId.ToString(), relativePath)
            : Path.Combine(ownerUserId.ToString(), legacyDirectory, relativePath);
        var destinationPath = mediaPathResolver.ResolveFilePath(ownerRelativePath);
        var newUrl = PrivateMediaPrefix + ownerRelativePath.Replace(Path.DirectorySeparatorChar, '/');
        var filePlanKey = sourcePath + "\n" + destinationPath;
        filePlans.TryAdd(filePlanKey, new FileMigrationPlan(sourcePath, destinationPath));
        return new UrlRewriteResult(newUrl, Changed: true);
    }

    private static IReadOnlyList<string> ParseUrlCollection(string rawValue)
    {
        try
        {
            using var document = JsonDocument.Parse(rawValue);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return document.RootElement.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .ToArray();
            }
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return [document.RootElement.GetString()!];
            }
        }
        catch (JsonException)
        {
            // Legacy rows may contain one URL or a comma-separated URL list.
        }

        return rawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool TryResolveLegacyPath(string? rawUrl, out string legacyDirectory, out string relativePath)
    {
        legacyDirectory = string.Empty;
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        var value = rawUrl.Trim().Replace('\\', '/');
        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
        {
            value = absoluteUri.AbsolutePath;
        }
        else
        {
            value = value.Split('?', '#')[0];
        }

        value = Uri.UnescapeDataString(value);
        foreach (var candidate in new[] { "ai-images", "nano-banana2-images" })
        {
            var prefix = "/" + candidate + "/";
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidatePath = value[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(candidatePath)
                || Path.IsPathRooted(candidatePath)
                || candidatePath.Split(Path.DirectorySeparatorChar).Any(x => x is ".." or "."))
            {
                throw new InvalidOperationException($"Unsafe legacy AI media URL: {rawUrl}");
            }

            legacyDirectory = candidate;
            relativePath = candidatePath;
            return true;
        }

        return false;
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"AI media path escapes its storage root: {relativePath}");
        }
        return fullPath;
    }

    private static IEnumerable<string> EnumerateLegacyFiles(string sourceWebRoot)
    {
        foreach (var directory in new[] { "ai-images", "nano-banana2-images" })
        {
            var path = Path.Combine(sourceWebRoot, directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                yield return Path.GetFullPath(file);
            }
        }
    }

    private static bool FilesMatch(string firstPath, string secondPath)
    {
        var firstInfo = new FileInfo(firstPath);
        var secondInfo = new FileInfo(secondPath);
        if (firstInfo.Length != secondInfo.Length)
        {
            return false;
        }

        using var first = File.OpenRead(firstPath);
        using var second = File.OpenRead(secondPath);
        return SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second));
    }

    private sealed class MigrationTask
    {
        public long Id { get; init; }
        public long UserId { get; init; }
        public string? ResultUrls { get; init; }
        public string? ReferenceImageUrls { get; init; }
        public string? MaskImageUrl { get; init; }
    }

    private sealed class MigrationFavorite
    {
        public long Id { get; init; }
        public long TaskId { get; init; }
        public string ImageUrl { get; init; } = string.Empty;
    }

    private sealed record TaskMigrationPlan(long Id, string? ResultUrls, string? ReferenceImageUrls, string? MaskImageUrl);
    private sealed record FavoriteMigrationPlan(long Id, string ImageUrl);
    private sealed record FileMigrationPlan(string SourcePath, string DestinationPath);
    private sealed record UrlRewriteResult(string? Value, bool Changed);
}

public sealed record AiMediaMigrationResult(
    int UpdatedTaskCount,
    int UpdatedFavoriteCount,
    int CopiedFileCount,
    int OrphanLegacyFileCount,
    bool DryRun);
