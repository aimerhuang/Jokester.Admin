using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Models.PromptLibrary;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class PromptLibrarySyncRunner(
    ISqlSugarClient db,
    IPromptLibrarySourceClient sourceClient,
    IPromptLibraryImageStore imageStore,
    IOptions<PromptLibraryOptions> options,
    ILogger<PromptLibrarySyncRunner> logger) : IPromptLibrarySyncRunner
{
    private readonly PromptLibraryOptions _options = options.Value;

    public async Task RunAsync(PromptLibrarySyncTrigger trigger, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var run = new PromptLibrarySyncRunEntity
        {
            Source = _options.Source,
            Status = PromptLibrarySyncStatuses.Running,
            StartedAt = LocalNow()
        };
        run.Id = await db.Insertable(run).ExecuteReturnBigIdentityAsync();

        var stagingDirectory = string.Empty;
        var parsedCount = 0;
        var selectedCount = 0;
        var downloadedCount = 0;
        var reusedImageCount = 0;
        var failedImageCount = 0;
        string? warningMessage = null;
        using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalTimeout.CancelAfter(TimeSpan.FromSeconds(_options.TotalTimeoutSeconds));

        try
        {
            var stableSourceItems = await db.Queryable<PromptLibraryItemEntity>()
                .Where(x => x.Source == _options.Source)
                .ToListAsync(totalTimeout.Token);
            var activeItems = stableSourceItems
                .Where(item => item.IsActive)
                .OrderBy(item => item.SourcePosition)
                .ToList();
            var activeSnapshotIds = activeItems
                .Select(item => item.SnapshotId)
                .Distinct()
                .ToArray();
            var activeSnapshotHealthy = activeItems.Count == _options.TargetCount
                && activeSnapshotIds.Length == 1
                && activeItems.All(item => imageStore.IsStoredImageAvailable(item.CoverLocalPath));
            var activeSnapshotRun = activeSnapshotHealthy
                ? await db.Queryable<PromptLibrarySyncRunEntity>()
                    .FirstAsync(
                        x => x.Id == activeSnapshotIds[0]
                            && x.Source == _options.Source
                            && x.Status == PromptLibrarySyncStatuses.Succeeded,
                        totalTimeout.Token)
                : null;
            var sourceSnapshot = await sourceClient.FetchSnapshotAsync(totalTimeout.Token);
            parsedCount = sourceSnapshot.CandidateCount;
            warningMessage = BuildSourceWarning(sourceSnapshot);
            if (sourceSnapshot.Items.Count < _options.TargetCount)
            {
                throw new InvalidDataException(
                    $"Prompt API returned {sourceSnapshot.Items.Count} usable Chinese prompt items; expected at least {_options.TargetCount}.");
            }

            var selectedItems = sourceSnapshot.Items
                .OrderBy(item => item.SourcePosition)
                .Take(_options.TargetCount)
                .ToArray();
            selectedCount = selectedItems.Length;
            if (activeSnapshotHealthy
                && !string.IsNullOrWhiteSpace(sourceSnapshot.ContentHash)
                && string.Equals(
                    sourceSnapshot.ContentHash,
                    activeSnapshotRun?.SourceContentHash,
                    StringComparison.Ordinal))
            {
                await CompleteNotModifiedAsync(
                    run.Id,
                    sourceSnapshot.ContentHash,
                    warningMessage,
                    activeItems.Count,
                    parsedCount,
                    totalTimeout.Token);
                await TryCleanupAsync(run.Id);
                return;
            }
            var existingSourceKeys = stableSourceItems
                .Select(item => new ExistingPromptLibrarySourceKey(
                    item.SourceKey,
                    item.SourceUrl,
                    item.PromptHash,
                    item.Title,
                    item.ExternalNo,
                    item.ExternalOccurrence,
                    item.SourcePosition,
                    item.IsActive))
                .ToArray();
            var sourceKeyAssignments = PromptLibrarySourceKeyFactory.CreateAssignments(
                selectedItems,
                existingSourceKeys);

            stagingDirectory = imageStore.CreateStagingDirectory(run.Id);
            var reusableImages = activeItems
                .Where(item => !string.IsNullOrWhiteSpace(item.CoverSourceUrl)
                    && imageStore.IsStoredImageAvailable(item.CoverLocalPath))
                .GroupBy(item => item.CoverSourceUrl, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().CoverLocalPath, StringComparer.Ordinal);
            var sourceRunIds = await db.Queryable<PromptLibrarySyncRunEntity>()
                .Where(x => x.Source == _options.Source)
                .Select(x => x.Id)
                .ToListAsync(totalTimeout.Token);
            var historicalImages = await db.Queryable<PromptLibraryItemVersionEntity>()
                .Where(x => sourceRunIds.Contains(x.SnapshotId))
                .Select(x => new PromptImageReference
                {
                    SourceUrl = x.CoverSourceUrl,
                    LocalPath = x.CoverLocalPath
                })
                .ToListAsync(totalTimeout.Token);
            foreach (var historicalImage in historicalImages)
            {
                if (!string.IsNullOrWhiteSpace(historicalImage.SourceUrl)
                    && !reusableImages.ContainsKey(historicalImage.SourceUrl)
                    && imageStore.IsStoredImageAvailable(historicalImage.LocalPath))
                {
                    reusableImages.Add(historicalImage.SourceUrl, historicalImage.LocalPath);
                }
            }
            var preparedItems = new PreparedPromptItem[selectedItems.Length];
            using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token);
            using var downloadConcurrency = new SemaphoreSlim(_options.DownloadConcurrency);
            var downloadTasks = selectedItems.Select(async (item, index) =>
            {
                await downloadConcurrency.WaitAsync(downloadCancellation.Token);
                try
                {
                    reusableImages.TryGetValue(item.CoverSourceUrl, out var reusablePath);
                    var storedImage = await imageStore.PrepareAsync(
                        item.ExternalNo,
                        item.CoverSourceUrl,
                        reusablePath,
                        stagingDirectory,
                        downloadCancellation.Token);
                    if (storedImage.Reused)
                    {
                        Interlocked.Increment(ref reusedImageCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref downloadedCount);
                    }

                    preparedItems[index] = PrepareItem(
                        item,
                        sourceKeyAssignments[index].ExternalOccurrence,
                        sourceKeyAssignments[index].SourceKey,
                        storedImage.RelativePath);
                }
                catch
                {
                    Interlocked.Increment(ref failedImageCount);
                    await downloadCancellation.CancelAsync();
                    throw;
                }
                finally
                {
                    downloadConcurrency.Release();
                }
            }).ToArray();
            await Task.WhenAll(downloadTasks);

            if (preparedItems.Any(item => item is null)
                || preparedItems.Any(item => !imageStore.IsStoredImageAvailable(item.CoverLocalPath)))
            {
                throw new InvalidDataException("One or more prompt cover files were not prepared successfully.");
            }

            await PublishSnapshotAsync(
                run,
                preparedItems,
                sourceSnapshot.ContentHash,
                parsedCount,
                downloadedCount,
                reusedImageCount,
                warningMessage,
                totalTimeout.Token);

            await TryCleanupAsync(run.Id);
            logger.LogInformation(
                "Prompt library synchronization succeeded. RunId={RunId}, Trigger={Trigger}, ItemCount={ItemCount}, Downloaded={Downloaded}, Reused={Reused}",
                run.Id,
                trigger,
                selectedCount,
                downloadedCount,
                reusedImageCount);
        }
        catch (Exception ex)
        {
            var message = totalTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                ? "Prompt library synchronization exceeded its total timeout."
                : SanitizeError(ex);
            await MarkFailedAsync(
                run.Id,
                parsedCount,
                selectedCount,
                downloadedCount,
                reusedImageCount,
                failedImageCount,
                message,
                warningMessage);

            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogError(
                "Prompt library synchronization failed. RunId={RunId}, Trigger={Trigger}, FailureType={FailureType}",
                run.Id,
                trigger,
                ex.GetType().Name);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(stagingDirectory))
            {
                try
                {
                    imageStore.DeleteStagingDirectory(stagingDirectory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.LogWarning(
                        "Prompt sync staging cleanup failed. RunId={RunId}, FailureType={FailureType}",
                        run.Id,
                        ex.GetType().Name);
                }
            }
        }
    }

    private async Task PublishSnapshotAsync(
        PromptLibrarySyncRunEntity run,
        IReadOnlyList<PreparedPromptItem> items,
        string contentHash,
        int parsedCount,
        int downloadedCount,
        int reusedImageCount,
        string? warningMessage,
        CancellationToken cancellationToken)
    {
        var now = LocalNow();
        await db.Ado.BeginTranAsync();
        try
        {
            var stableItems = await db.Queryable<PromptLibraryItemEntity>()
                .Where(x => x.Source == _options.Source)
                .ToListAsync(cancellationToken);
            var stableBySourceKey = stableItems.ToDictionary(
                item => item.SourceKey,
                StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (stableBySourceKey.ContainsKey(item.SourceKey))
                {
                    continue;
                }

                var entity = MapNewStableItem(item, run.Id, now);
                entity.Id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
                stableItems.Add(entity);
                stableBySourceKey.Add(entity.SourceKey, entity);
            }

            await db.Updateable<PromptLibraryItemEntity>()
                .SetColumns(x => new PromptLibraryItemEntity { IsActive = false, UpdatedAt = now })
                .Where(x => x.Source == _options.Source && x.IsActive)
                .ExecuteCommandAsync(cancellationToken);

            var versions = new List<PromptLibraryItemVersionEntity>(items.Count);
            foreach (var item in items)
            {
                var stable = stableBySourceKey[item.SourceKey];
                var affected = await db.Updateable<PromptLibraryItemEntity>()
                    .SetColumns(x => new PromptLibraryItemEntity
                    {
                        ExternalNo = item.ExternalNo,
                        ExternalOccurrence = item.ExternalOccurrence,
                        Title = item.Title,
                        Description = item.Description,
                        PromptText = item.PromptText,
                        PromptHash = item.PromptHash,
                        CoverSourceUrl = item.CoverSourceUrl,
                        CoverLocalPath = item.CoverLocalPath,
                        AuthorName = item.AuthorName,
                        AuthorUrl = item.AuthorUrl,
                        SourceUrl = item.SourceUrl,
                        SourcePublishedAt = item.SourcePublishedAt,
                        Language = item.Language,
                        SourcePosition = item.SourcePosition,
                        SnapshotId = run.Id,
                        IsActive = true,
                        UpdatedAt = now
                    })
                    .Where(x => x.Id == stable.Id && x.Source == _options.Source)
                    .ExecuteCommandAsync(cancellationToken);
                if (affected != 1)
                {
                    throw new InvalidOperationException($"Prompt item {item.ExternalNo} could not be activated.");
                }

                versions.Add(MapVersion(item, stable.Id, run.Id, now));
            }

            if (versions.Count != _options.TargetCount)
            {
                throw new InvalidOperationException("Prompt snapshot item count changed before publication.");
            }
            await db.Insertable(versions).ExecuteCommandAsync(cancellationToken);

            var runUpdated = await db.Updateable<PromptLibrarySyncRunEntity>()
                .SetColumns(x => new PromptLibrarySyncRunEntity
                {
                    SourceContentHash = contentHash,
                    Status = PromptLibrarySyncStatuses.Succeeded,
                    ParsedCount = parsedCount,
                    SelectedCount = items.Count,
                    DownloadedCount = downloadedCount,
                    ReusedImageCount = reusedImageCount,
                    FailedImageCount = 0,
                    FinishedAt = now,
                    ErrorMessage = null,
                    WarningMessage = warningMessage
                })
                .Where(x => x.Id == run.Id && x.Status == PromptLibrarySyncStatuses.Running)
                .ExecuteCommandAsync(cancellationToken);
            if (runUpdated != 1)
            {
                throw new InvalidOperationException("Prompt sync run could not be finalized.");
            }

            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task CompleteNotModifiedAsync(
        long runId,
        string contentHash,
        string? warningMessage,
        int activeItemCount,
        int parsedCount,
        CancellationToken cancellationToken)
    {
        await db.Updateable<PromptLibrarySyncRunEntity>()
            .SetColumns(x => new PromptLibrarySyncRunEntity
            {
                SourceContentHash = contentHash,
                Status = PromptLibrarySyncStatuses.NotModified,
                ParsedCount = parsedCount,
                SelectedCount = activeItemCount,
                DownloadedCount = 0,
                ReusedImageCount = activeItemCount,
                FailedImageCount = 0,
                FinishedAt = LocalNow(),
                ErrorMessage = null,
                WarningMessage = warningMessage
            })
            .Where(x => x.Id == runId && x.Status == PromptLibrarySyncStatuses.Running)
            .ExecuteCommandAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        long runId,
        int parsedCount,
        int selectedCount,
        int downloadedCount,
        int reusedImageCount,
        int failedImageCount,
        string message,
        string? warningMessage)
    {
        try
        {
            await db.Updateable<PromptLibrarySyncRunEntity>()
                .SetColumns(x => new PromptLibrarySyncRunEntity
                {
                    Status = PromptLibrarySyncStatuses.Failed,
                    ParsedCount = parsedCount,
                    SelectedCount = selectedCount,
                    DownloadedCount = downloadedCount,
                    ReusedImageCount = reusedImageCount,
                    FailedImageCount = failedImageCount,
                    FinishedAt = LocalNow(),
                    ErrorMessage = message,
                    WarningMessage = warningMessage
                })
                .Where(x => x.Id == runId && x.Status == PromptLibrarySyncStatuses.Running)
                .ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Prompt sync run failure could not be persisted. RunId={RunId}, FailureType={FailureType}",
                runId,
                ex.GetType().Name);
        }
    }

    private async Task CleanupOldSnapshotsAndImagesAsync(CancellationToken cancellationToken)
    {
        var cutoff = LocalNow().AddDays(-_options.ImageRetainDays);
        var fileCutoffUtc = DateTime.UtcNow.AddDays(-_options.ImageRetainDays);
        var sourceSnapshotIds = await db.Queryable<PromptLibrarySyncRunEntity>()
            .Where(x => x.Source == _options.Source)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var snapshotSummaries = await db.Queryable<PromptLibraryItemVersionEntity>()
            .Where(x => sourceSnapshotIds.Contains(x.SnapshotId))
            .GroupBy(x => x.SnapshotId)
            .Select(x => new SnapshotSummary
            {
                SnapshotId = x.SnapshotId,
                CreatedAt = SqlFunc.AggregateMax(x.CreatedAt)
            })
            .OrderByDescending(x => x.SnapshotId)
            .ToListAsync(cancellationToken);
        var retainedSnapshotIds = snapshotSummaries
            .Take(_options.KeepSnapshots)
            .Select(snapshot => snapshot.SnapshotId)
            .ToHashSet();
        var activeSnapshotIds = await db.Queryable<PromptLibraryItemEntity>()
            .Where(x => x.Source == _options.Source && x.IsActive)
            .Select(x => x.SnapshotId)
            .Distinct()
            .ToListAsync(cancellationToken);
        retainedSnapshotIds.UnionWith(activeSnapshotIds);
        var expiredSnapshotIds = snapshotSummaries
            .Where(snapshot => !retainedSnapshotIds.Contains(snapshot.SnapshotId) && snapshot.CreatedAt < cutoff)
            .Select(snapshot => snapshot.SnapshotId)
            .ToArray();
        if (expiredSnapshotIds.Length > 0)
        {
            await db.Deleteable<PromptLibraryItemVersionEntity>()
                .Where(version => expiredSnapshotIds.Contains(version.SnapshotId))
                .ExecuteCommandAsync(cancellationToken);
        }

        var activePaths = await db.Queryable<PromptLibraryItemEntity>()
            .Where(x => x.Source == _options.Source && x.IsActive)
            .Select(x => x.CoverLocalPath)
            .ToListAsync(cancellationToken);
        var versionPaths = await db.Queryable<PromptLibraryItemVersionEntity>()
            .Where(x => sourceSnapshotIds.Contains(x.SnapshotId))
            .Select(x => x.CoverLocalPath)
            .Distinct()
            .ToListAsync(cancellationToken);
        var referencedPaths = activePaths
            .Concat(versionPaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        await imageStore.CleanupAsync(referencedPaths, fileCutoffUtc, cancellationToken);
    }

    private async Task TryCleanupAsync(long runId)
    {
        try
        {
            await CleanupOldSnapshotsAndImagesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Prompt library synchronization completed, but retention cleanup failed. RunId={RunId}, FailureType={FailureType}",
                runId,
                ex.GetType().Name);
        }
    }

    private static PreparedPromptItem PrepareItem(
        PromptLibrarySourceItem item,
        int externalOccurrence,
        string sourceKey,
        string localPath) => new()
    {
        ExternalNo = item.ExternalNo,
        ExternalOccurrence = externalOccurrence,
        SourceKey = sourceKey,
        Title = Limit(item.Title.Trim(), 300),
        Description = item.Description.Trim(),
        PromptText = item.PromptText.Trim(),
        PromptHash = HashText(item.PromptText.Trim()),
        CoverSourceUrl = Limit(item.CoverSourceUrl, 1000),
        CoverLocalPath = Limit(localPath, 500),
        AuthorName = LimitNullable(item.AuthorName, 200),
        AuthorUrl = LimitNullable(item.AuthorUrl, 1000),
        SourceUrl = LimitNullable(item.SourceUrl, 1000),
        SourcePublishedAt = ParsePublishedAt(item.Published),
        Language = LimitNullable(item.Language, 50),
        SourcePosition = item.SourcePosition
    };

    private PromptLibraryItemEntity MapNewStableItem(PreparedPromptItem item, long snapshotId, DateTime now) => new()
    {
        Source = _options.Source,
        SourceKey = item.SourceKey,
        ExternalNo = item.ExternalNo,
        ExternalOccurrence = item.ExternalOccurrence,
        Title = item.Title,
        Description = item.Description,
        PromptText = item.PromptText,
        PromptHash = item.PromptHash,
        CoverSourceUrl = item.CoverSourceUrl,
        CoverLocalPath = item.CoverLocalPath,
        AuthorName = item.AuthorName,
        AuthorUrl = item.AuthorUrl,
        SourceUrl = item.SourceUrl,
        SourcePublishedAt = item.SourcePublishedAt,
        Language = item.Language,
        SourcePosition = item.SourcePosition,
        SnapshotId = snapshotId,
        IsActive = false,
        CreatedAt = now
    };

    private static PromptLibraryItemVersionEntity MapVersion(
        PreparedPromptItem item,
        long promptId,
        long snapshotId,
        DateTime now) => new()
    {
        SnapshotId = snapshotId,
        PromptId = promptId,
        ExternalNo = item.ExternalNo,
        ExternalOccurrence = item.ExternalOccurrence,
        Title = item.Title,
        Description = item.Description,
        PromptText = item.PromptText,
        PromptHash = item.PromptHash,
        CoverSourceUrl = item.CoverSourceUrl,
        CoverLocalPath = item.CoverLocalPath,
        AuthorName = item.AuthorName,
        AuthorUrl = item.AuthorUrl,
        SourceUrl = item.SourceUrl,
        SourcePublishedAt = item.SourcePublishedAt,
        Language = item.Language,
        SourcePosition = item.SourcePosition,
        CreatedAt = now
    };

    private static DateTime? ParsePublishedAt(string? value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result)
            ? result
            : null;
    }

    private static string HashText(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? LimitNullable(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return Limit(normalized, maxLength);
    }

    private static string SanitizeError(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? "Prompt library synchronization failed."
            : exception.Message;
        return Limit(message.Replace('\r', ' ').Replace('\n', ' '), 2000);
    }

    private static string? BuildSourceWarning(PromptLibrarySourceSnapshot snapshot)
    {
        if (snapshot.SkippedCount == 0 && snapshot.Diagnostics.Count == 0)
        {
            return null;
        }

        var diagnosticSummary = string.Join(
            ", ",
            snapshot.Diagnostics.Take(20));
        return Limit(
            $"skipped={snapshot.SkippedCount}; diagnostics={snapshot.Diagnostics.Count}; {diagnosticSummary}",
            4000);
    }

    private static DateTime LocalNow() => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime;

    private sealed class PreparedPromptItem
    {
        public int ExternalNo { get; init; }
        public int ExternalOccurrence { get; init; }
        public string SourceKey { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string PromptText { get; init; } = string.Empty;
        public string PromptHash { get; init; } = string.Empty;
        public string CoverSourceUrl { get; init; } = string.Empty;
        public string CoverLocalPath { get; init; } = string.Empty;
        public string? AuthorName { get; init; }
        public string? AuthorUrl { get; init; }
        public string? SourceUrl { get; init; }
        public DateTime? SourcePublishedAt { get; init; }
        public string? Language { get; init; }
        public int SourcePosition { get; init; }
    }

    private sealed class SnapshotSummary
    {
        public long SnapshotId { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    private sealed class PromptImageReference
    {
        public string SourceUrl { get; init; } = string.Empty;
        public string LocalPath { get; init; } = string.Empty;
    }
}
