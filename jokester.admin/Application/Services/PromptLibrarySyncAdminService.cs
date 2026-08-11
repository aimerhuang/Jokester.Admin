using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Prompts;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class PromptLibrarySyncAdminService(
    ISqlSugarClient db,
    IPromptLibrarySyncQueue queue,
    IOptions<PromptLibraryOptions> options,
    IServiceProvider serviceProvider,
    ILogger<PromptLibrarySyncAdminService> logger) : IPromptLibrarySyncAdminService
{
    private const long MinimumFreeBytes = 2L * 1024 * 1024 * 1024;

    public async Task<PromptLibrarySyncStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var activeItems = await db.Queryable<PromptLibraryItemEntity>()
            .Where(x => x.Source == settings.Source && x.IsActive)
            .OrderBy(x => x.SourcePosition)
            .ToListAsync(cancellationToken);
        var recentRuns = await db.Queryable<PromptLibrarySyncRunEntity>()
            .Where(x => x.Source == settings.Source)
            .OrderByDescending(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var latestRun = recentRuns.FirstOrDefault();
        var latestSuccessfulRun = recentRuns.FirstOrDefault(x =>
            x.Status == PromptLibrarySyncStatuses.Succeeded
            || x.Status == PromptLibrarySyncStatuses.NotModified);
        var consecutiveFailures = recentRuns
            .Where(x => x.Status != PromptLibrarySyncStatuses.Running)
            .TakeWhile(x => x.Status == PromptLibrarySyncStatuses.Failed)
            .Count();
        var warnings = new List<string>();
        var snapshotIds = activeItems.Select(x => x.SnapshotId).Distinct().ToArray();
        var currentSnapshotId = snapshotIds.Length == 1 ? snapshotIds[0] : (long?)null;
        if (snapshotIds.Length > 1)
        {
            warnings.Add("Active prompt items refer to more than one snapshot.");
        }

        PromptLibrarySyncRunEntity? currentSnapshotRun = null;
        if (currentSnapshotId.HasValue)
        {
            currentSnapshotRun = recentRuns.FirstOrDefault(x => x.Id == currentSnapshotId.Value)
                ?? await db.Queryable<PromptLibrarySyncRunEntity>()
                    .FirstAsync(
                        x => x.Id == currentSnapshotId.Value && x.Source == settings.Source,
                        cancellationToken);
        }

        var coverCount = 0;
        long imageBytes = 0;
        long? diskFreeBytes = null;
        if (settings.Enabled && !string.IsNullOrWhiteSpace(settings.ImageRoot))
        {
            try
            {
                var imageStore = serviceProvider.GetRequiredService<IPromptLibraryImageStore>();
                coverCount = activeItems.Count(item => imageStore.IsStoredImageAvailable(item.CoverLocalPath));
                imageBytes = imageStore.GetStoredBytes();
                diskFreeBytes = imageStore.GetAvailableBytes();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(
                    "Prompt library image storage health check failed. FailureType={FailureType}",
                    ex.GetType().Name);
                warnings.Add("Prompt image storage cannot be inspected.");
            }
        }

        var missingCoverCount = Math.Max(0, activeItems.Count - coverCount);
        if (consecutiveFailures >= 3)
        {
            warnings.Add("Prompt synchronization has failed at least three consecutive times.");
        }
        if (latestSuccessfulRun?.FinishedAt is not { } lastSuccessfulAt
            || LocalNow() - lastSuccessfulAt > TimeSpan.FromHours(36))
        {
            warnings.Add("No prompt synchronization has succeeded in the last 36 hours.");
        }
        if (activeItems.Count != settings.TargetCount)
        {
            warnings.Add($"Active prompt item count is {activeItems.Count}; expected {settings.TargetCount}.");
        }
        if (missingCoverCount > 0)
        {
            warnings.Add($"{missingCoverCount} active prompt covers are missing.");
        }
        if (diskFreeBytes.HasValue && diskFreeBytes.Value < MinimumFreeBytes)
        {
            warnings.Add("Prompt image storage has less than 2GB free space.");
        }
        if (!string.IsNullOrWhiteSpace(latestSuccessfulRun?.WarningMessage))
        {
            warnings.Add($"Latest successful prompt synchronization diagnostics: {latestSuccessfulRun.WarningMessage}");
        }

        return new PromptLibrarySyncStatusDto
        {
            Enabled = settings.Enabled,
            IsQueued = queue.IsQueued,
            IsRunning = queue.IsRunning,
            IsSwitchingSnapshot = queue.IsSwitchingSnapshot,
            CurrentSnapshotId = currentSnapshotId,
            ActiveItemCount = activeItems.Count,
            LocalCoverCount = coverCount,
            MissingCoverCount = missingCoverCount,
            ImageBytes = imageBytes,
            DiskFreeBytes = diskFreeBytes,
            LastSuccessfulAt = latestSuccessfulRun?.FinishedAt,
            CurrentContentHash = currentSnapshotRun?.SourceContentHash,
            ConsecutiveFailureCount = consecutiveFailures,
            LatestRun = latestRun is null ? null : MapRun(latestRun),
            Warnings = warnings
        };
    }

    public QueuePromptLibrarySyncResponse QueueRun()
    {
        if (!options.Value.Enabled)
        {
            throw new AppException(ErrorCodes.ServiceUnavailable, "Prompt library synchronization is disabled.");
        }

        var accepted = queue.TryEnqueue(PromptLibrarySyncTrigger.Manual);
        return new QueuePromptLibrarySyncResponse
        {
            Accepted = accepted,
            IsQueued = queue.IsQueued,
            IsRunning = queue.IsRunning,
            IsSwitchingSnapshot = queue.IsSwitchingSnapshot
        };
    }

    public async Task<SwitchPromptLibrarySnapshotResponse> SwitchSnapshotAsync(
        long snapshotId,
        CancellationToken cancellationToken)
    {
        if (snapshotId <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "snapshotId must be a positive integer");
        }

        using var switchLease = AcquireSnapshotSwitchLease();
        var settings = options.Value;
        var targetRun = await db.Queryable<PromptLibrarySyncRunEntity>()
            .FirstAsync(
                x => x.Id == snapshotId
                    && x.Source == settings.Source
                    && x.Status == PromptLibrarySyncStatuses.Succeeded,
                cancellationToken);
        if (targetRun is null)
        {
            throw new NotFoundException($"Succeeded prompt snapshot does not exist: {snapshotId}");
        }

        var versions = await db.Queryable<PromptLibraryItemVersionEntity>()
            .Where(x => x.SnapshotId == snapshotId)
            .OrderBy(x => x.SourcePosition)
            .OrderBy(x => x.PromptId)
            .ToListAsync(cancellationToken);
        var targetPromptIds = versions.Select(x => x.PromptId).ToHashSet();
        if (versions.Count != settings.TargetCount || targetPromptIds.Count != settings.TargetCount)
        {
            throw new ConflictException(
                $"Prompt snapshot {snapshotId} contains {versions.Count} versions; expected {settings.TargetCount} unique items.");
        }

        IPromptLibraryImageStore imageStore;
        try
        {
            imageStore = serviceProvider.GetRequiredService<IPromptLibraryImageStore>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                "Prompt snapshot image storage could not be resolved. SnapshotId={SnapshotId}, FailureType={FailureType}",
                snapshotId,
                ex.GetType().Name);
            throw new ConflictException("Prompt image storage cannot be inspected.");
        }

        var missingCoverCount = versions.Count(version =>
            !imageStore.IsStoredImageAvailable(version.CoverLocalPath));
        if (missingCoverCount > 0)
        {
            throw new ConflictException(
                $"Prompt snapshot {snapshotId} has {missingCoverCount} missing or invalid local covers.");
        }

        var stableItems = await db.Queryable<PromptLibraryItemEntity>()
            .Where(x => x.Source == settings.Source)
            .ToListAsync(cancellationToken);
        var stableById = stableItems.ToDictionary(x => x.Id);
        if (targetPromptIds.Any(promptId => !stableById.ContainsKey(promptId)))
        {
            throw new ConflictException($"Prompt snapshot {snapshotId} refers to missing stable items.");
        }

        var activeItems = stableItems.Where(x => x.IsActive).ToArray();
        var activeSnapshotIds = activeItems.Select(x => x.SnapshotId).Distinct().ToArray();
        var previousSnapshotId = activeSnapshotIds.Length == 1 ? activeSnapshotIds[0] : (long?)null;
        var alreadyActive = activeItems.Length == settings.TargetCount
            && activeItems.All(x => x.SnapshotId == snapshotId)
            && targetPromptIds.SetEquals(activeItems.Select(x => x.Id));
        if (alreadyActive)
        {
            return new SwitchPromptLibrarySnapshotResponse
            {
                SnapshotId = snapshotId,
                PreviousSnapshotId = snapshotId,
                Changed = false,
                ActiveItemCount = activeItems.Length
            };
        }

        var now = LocalNow();
        await db.Ado.BeginTranAsync();
        try
        {
            await db.Updateable<PromptLibraryItemEntity>()
                .SetColumns(x => new PromptLibraryItemEntity { IsActive = false, UpdatedAt = now })
                .Where(x => x.Source == settings.Source && x.IsActive)
                .ExecuteCommandAsync(cancellationToken);

            foreach (var version in versions)
            {
                var affected = await db.Updateable<PromptLibraryItemEntity>()
                    .SetColumns(x => new PromptLibraryItemEntity
                    {
                        ExternalNo = version.ExternalNo,
                        ExternalOccurrence = version.ExternalOccurrence,
                        Title = version.Title,
                        Description = version.Description,
                        PromptText = version.PromptText,
                        PromptHash = version.PromptHash,
                        CoverSourceUrl = version.CoverSourceUrl,
                        CoverLocalPath = version.CoverLocalPath,
                        AuthorName = version.AuthorName,
                        AuthorUrl = version.AuthorUrl,
                        SourceUrl = version.SourceUrl,
                        SourcePublishedAt = version.SourcePublishedAt,
                        Language = version.Language,
                        SourcePosition = version.SourcePosition,
                        SnapshotId = snapshotId,
                        IsActive = true,
                        UpdatedAt = now
                    })
                    .Where(x => x.Id == version.PromptId && x.Source == settings.Source)
                    .ExecuteCommandAsync(cancellationToken);
                if (affected != 1)
                {
                    throw new InvalidOperationException(
                        $"Prompt item {version.PromptId} could not be restored from snapshot {snapshotId}.");
                }
            }

            var activeCount = await db.Queryable<PromptLibraryItemEntity>()
                .Where(x => x.Source == settings.Source && x.IsActive && x.SnapshotId == snapshotId)
                .CountAsync(cancellationToken);
            if (activeCount != settings.TargetCount)
            {
                throw new InvalidOperationException(
                    $"Prompt snapshot {snapshotId} activated {activeCount} items; expected {settings.TargetCount}.");
            }

            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        logger.LogInformation(
            "Prompt library snapshot activated. SnapshotId={SnapshotId}, PreviousSnapshotId={PreviousSnapshotId}, ItemCount={ItemCount}",
            snapshotId,
            previousSnapshotId,
            versions.Count);
        return new SwitchPromptLibrarySnapshotResponse
        {
            SnapshotId = snapshotId,
            PreviousSnapshotId = previousSnapshotId,
            Changed = true,
            ActiveItemCount = versions.Count
        };
    }

    private IDisposable AcquireSnapshotSwitchLease()
    {
        if (!queue.TryBeginSnapshotSwitch())
        {
            throw new ConflictException(
                "Prompt snapshot cannot be switched while synchronization is queued or running, or another snapshot switch is active.");
        }

        return new SnapshotSwitchLease(queue);
    }

    private static PromptLibrarySyncRunDto MapRun(PromptLibrarySyncRunEntity run) => new()
    {
        Id = run.Id,
        Source = run.Source,
        Status = run.Status,
        SourceContentHash = run.SourceContentHash,
        ParsedCount = run.ParsedCount,
        SelectedCount = run.SelectedCount,
        DownloadedCount = run.DownloadedCount,
        ReusedImageCount = run.ReusedImageCount,
        FailedImageCount = run.FailedImageCount,
        StartedAt = run.StartedAt,
        FinishedAt = run.FinishedAt,
        ErrorMessage = run.ErrorMessage,
        WarningMessage = run.WarningMessage
    };

    private static DateTime LocalNow() => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime;

    private sealed class SnapshotSwitchLease(IPromptLibrarySyncQueue queue) : IDisposable
    {
        private IPromptLibrarySyncQueue? owner = queue;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.EndSnapshotSwitch();
    }
}

public static class PromptLibrarySyncStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string NotModified = "not_modified";
    public const string Failed = "failed";
}
