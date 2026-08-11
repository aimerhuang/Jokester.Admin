using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Models.PromptLibrary;
using jokester.admin.Application.Services;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class PromptLibrarySyncRunnerTests
{
    [Fact]
    public async Task RunAsync_PublishesApiSnapshotWithMatchingRunItemsAndVersions()
    {
        using var context = new RunnerTestContext(targetCount: 2);
        var snapshot = CreateSnapshot("A", itemCount: 2, candidateCount: 3, skippedCount: 1);
        context.SourceClient
            .Setup(x => x.FetchSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        await context.Runner.RunAsync(PromptLibrarySyncTrigger.Manual, CancellationToken.None);

        var run = await context.LatestRunAsync();
        Assert.Equal(PromptLibrarySyncStatuses.Succeeded, run.Status);
        Assert.Equal(snapshot.ContentHash, run.SourceContentHash);
        Assert.Equal(3, run.ParsedCount);
        Assert.Equal(2, run.SelectedCount);
        Assert.Equal(2, run.DownloadedCount);
        Assert.Equal(0, run.ReusedImageCount);
        Assert.NotNull(run.WarningMessage);

        var activeItems = await context.ActiveItemsAsync();
        Assert.Collection(
            activeItems,
            item => AssertPublishedItem(item, run.Id, "A", 1),
            item => AssertPublishedItem(item, run.Id, "A", 2));

        var versions = await context.Db.Queryable<PromptLibraryItemVersionEntity>()
            .Where(x => x.SnapshotId == run.Id)
            .OrderBy(x => x.SourcePosition)
            .ToListAsync();
        Assert.Collection(
            versions,
            version => AssertVersion(version, run.Id, activeItems[0].Id, "A", 1),
            version => AssertVersion(version, run.Id, activeItems[1].Id, "A", 2));
        context.ImageStore.Verify(x => x.PrepareAsync(
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_WhenSourceIsShort_KeepsOldActiveSnapshotAndMarksRunFailed()
    {
        using var context = new RunnerTestContext(targetCount: 2);
        var oldRun = context.SeedSuccessfulRun("hash-old");
        var oldItems = context.SeedActiveItems(oldRun.Id, "old", count: 2);
        var shortSnapshot = CreateSnapshot("short", itemCount: 1);
        context.SourceClient
            .Setup(x => x.FetchSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(shortSnapshot);

        await context.Runner.RunAsync(PromptLibrarySyncTrigger.Scheduled, CancellationToken.None);

        var failedRun = await context.LatestRunAsync();
        Assert.NotEqual(oldRun.Id, failedRun.Id);
        Assert.Equal(PromptLibrarySyncStatuses.Failed, failedRun.Status);
        Assert.Equal(1, failedRun.ParsedCount);
        Assert.Equal(0, failedRun.SelectedCount);
        Assert.Contains("expected at least 2", failedRun.ErrorMessage, StringComparison.Ordinal);

        var activeItems = await context.ActiveItemsAsync();
        Assert.Equal(oldItems.Select(item => item.Id), activeItems.Select(item => item.Id));
        Assert.All(activeItems, item =>
        {
            Assert.Equal(oldRun.Id, item.SnapshotId);
            Assert.StartsWith("old-title-", item.Title, StringComparison.Ordinal);
            Assert.True(item.IsActive);
        });
        context.ImageStore.Verify(x => x.CreateStagingDirectory(It.IsAny<long>()), Times.Never);
        context.ImageStore.Verify(x => x.PrepareAsync(
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_AfterManualRollbackToA_RepublishesUpstreamBInsteadOfReturningNotModified()
    {
        using var context = new RunnerTestContext(targetCount: 2);
        var snapshotA = CreateSnapshot("A", itemCount: 2);
        var snapshotB = CreateSnapshot("B", itemCount: 2);
        context.SourceClient
            .SetupSequence(x => x.FetchSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshotA)
            .ReturnsAsync(snapshotB)
            .ReturnsAsync(snapshotB);

        await context.Runner.RunAsync(PromptLibrarySyncTrigger.Manual, CancellationToken.None);
        var runA = await context.LatestRunAsync();
        Assert.Equal(PromptLibrarySyncStatuses.Succeeded, runA.Status);

        await context.Runner.RunAsync(PromptLibrarySyncTrigger.Manual, CancellationToken.None);
        var runB = await context.LatestRunAsync();
        Assert.Equal(PromptLibrarySyncStatuses.Succeeded, runB.Status);
        Assert.Equal(snapshotB.ContentHash, runB.SourceContentHash);

        context.ActivateSnapshot(runA.Id);
        var rolledBackItems = await context.ActiveItemsAsync();
        Assert.All(rolledBackItems, item => Assert.Equal(runA.Id, item.SnapshotId));
        Assert.All(rolledBackItems, item => Assert.StartsWith("A-title-", item.Title, StringComparison.Ordinal));

        await context.Runner.RunAsync(PromptLibrarySyncTrigger.Scheduled, CancellationToken.None);

        var runs = await context.Db.Queryable<PromptLibrarySyncRunEntity>()
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Equal(3, runs.Count);
        Assert.All(runs, run => Assert.Equal(PromptLibrarySyncStatuses.Succeeded, run.Status));
        var republishedB = runs[^1];
        Assert.Equal(snapshotB.ContentHash, republishedB.SourceContentHash);
        Assert.NotEqual(runB.Id, republishedB.Id);
        Assert.Equal(0, republishedB.DownloadedCount);
        Assert.Equal(2, republishedB.ReusedImageCount);

        var activeItems = await context.ActiveItemsAsync();
        Assert.All(activeItems, item => Assert.Equal(republishedB.Id, item.SnapshotId));
        Assert.Collection(
            activeItems,
            item => AssertPublishedItem(item, republishedB.Id, "B", 1),
            item => AssertPublishedItem(item, republishedB.Id, "B", 2));
        Assert.Equal(
            6,
            await context.Db.Queryable<PromptLibraryItemVersionEntity>().CountAsync());
        context.SourceClient.Verify(
            x => x.FetchSnapshotAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        context.ImageStore.Verify(x => x.PrepareAsync(
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Exactly(6));
    }

    [Fact]
    public async Task RunAsync_CleanupAlwaysRetainsTheActiveHistoricalSnapshot()
    {
        using var context = new RunnerTestContext(
            targetCount: 1,
            imageRetainDays: 7,
            keepSnapshots: 2);
        var activeRun = context.SeedSuccessfulRun("hash-A");
        var activeItem = Assert.Single(context.SeedActiveItems(activeRun.Id, "A", count: 1));
        context.SeedVersion(activeRun.Id, activeItem.Id, "A", DateTime.Now.AddDays(-30));

        var newerRun = context.SeedSuccessfulRun("hash-B");
        context.SeedVersion(newerRun.Id, activeItem.Id, "B", DateTime.Now.AddDays(-20));
        var newestRun = context.SeedSuccessfulRun("hash-C");
        context.SeedVersion(newestRun.Id, activeItem.Id, "C", DateTime.Now.AddDays(-10));
        context.SourceClient
            .Setup(x => x.FetchSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot("A", itemCount: 1));

        await context.Runner.RunAsync(PromptLibrarySyncTrigger.Scheduled, CancellationToken.None);

        var run = await context.LatestRunAsync();
        Assert.Equal(PromptLibrarySyncStatuses.NotModified, run.Status);
        Assert.Equal(
            1,
            await context.Db.Queryable<PromptLibraryItemVersionEntity>()
                .Where(x => x.SnapshotId == activeRun.Id)
                .CountAsync());
    }

    private static PromptLibrarySourceSnapshot CreateSnapshot(
        string version,
        int itemCount,
        int? candidateCount = null,
        int skippedCount = 0)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(number => new PromptLibrarySourceItem(
                $"stable-{number}",
                number,
                $"{version}-title-{number}",
                $"{version}-description-{number}",
                $"{version}-prompt-{number}",
                $"https://cms-assets.youmind.com/{version}-{number}.webp",
                $"author-{number}",
                $"https://youmind.example/authors/{number}",
                $"https://youmind.example/prompts/{number}",
                "2026-08-10T00:00:00Z",
                "zh-CN",
                number))
            .ToArray();
        var diagnostics = skippedCount == 0
            ? Array.Empty<string>()
            : Enumerable.Range(1, skippedCount)
                .Select(number => $"page=1;item={number};reason=filtered")
                .ToArray();
        return new PromptLibrarySourceSnapshot(
            items,
            candidateCount ?? itemCount,
            skippedCount,
            $"hash-{version}",
            diagnostics);
    }

    private static void AssertPublishedItem(
        PromptLibraryItemEntity item,
        long snapshotId,
        string version,
        int number)
    {
        Assert.Equal(snapshotId, item.SnapshotId);
        Assert.Equal(number, item.ExternalNo);
        Assert.Equal(number, item.SourcePosition);
        Assert.Equal($"{version}-title-{number}", item.Title);
        Assert.Equal($"{version}-description-{number}", item.Description);
        Assert.Equal($"{version}-prompt-{number}", item.PromptText);
        Assert.Equal($"https://cms-assets.youmind.com/{version}-{number}.webp", item.CoverSourceUrl);
        Assert.Equal($"stored/{version}-{number}.webp", item.CoverLocalPath);
        Assert.True(item.IsActive);
    }

    private static void AssertVersion(
        PromptLibraryItemVersionEntity version,
        long snapshotId,
        long promptId,
        string sourceVersion,
        int number)
    {
        Assert.Equal(snapshotId, version.SnapshotId);
        Assert.Equal(promptId, version.PromptId);
        Assert.Equal(number, version.SourcePosition);
        Assert.Equal($"{sourceVersion}-title-{number}", version.Title);
        Assert.Equal($"stored/{sourceVersion}-{number}.webp", version.CoverLocalPath);
    }

    private sealed class RunnerTestContext : IDisposable
    {
        private const string Source = "runner-test-source";

        public RunnerTestContext(
            int targetCount,
            int imageRetainDays = 3650,
            int keepSnapshots = 10)
        {
            SQLitePCL.Batteries_V2.Init();
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=:memory:",
                DbType = SqlSugar.DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute
            });
            CreateTables();

            SourceClient = new Mock<IPromptLibrarySourceClient>();
            ImageStore = new Mock<IPromptLibraryImageStore>();
            ImageStore.SetupGet(x => x.RootPath).Returns("test-root");
            ImageStore
                .Setup(x => x.CreateStagingDirectory(It.IsAny<long>()))
                .Returns<long>(runId => $"staging/{runId}");
            ImageStore
                .Setup(x => x.DeleteStagingDirectory(It.IsAny<string>()));
            ImageStore
                .Setup(x => x.IsStoredImageAvailable(It.IsAny<string?>()))
                .Returns(true);
            ImageStore
                .Setup(x => x.PrepareAsync(
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((int _, string sourceUrl, string? reusablePath, string _, CancellationToken _) =>
                {
                    var fileName = new Uri(sourceUrl).Segments[^1];
                    return Task.FromResult(new PromptStoredImage(
                        reusablePath ?? $"stored/{fileName}",
                        Reused: reusablePath is not null));
                });
            ImageStore
                .Setup(x => x.CleanupAsync(
                    It.IsAny<IReadOnlySet<string>>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Runner = new PromptLibrarySyncRunner(
                Db,
                SourceClient.Object,
                ImageStore.Object,
                Options.Create(new PromptLibraryOptions
                {
                    Enabled = true,
                    Source = Source,
                    TargetCount = targetCount,
                    TotalTimeoutSeconds = 30,
                    DownloadConcurrency = 2,
                    ImageRetainDays = imageRetainDays,
                    KeepSnapshots = keepSnapshots,
                    ImageRoot = Path.GetTempPath()
                }),
                NullLogger<PromptLibrarySyncRunner>.Instance);
        }

        public SqlSugarClient Db { get; }

        public Mock<IPromptLibrarySourceClient> SourceClient { get; }

        public Mock<IPromptLibraryImageStore> ImageStore { get; }

        public PromptLibrarySyncRunner Runner { get; }

        public async Task<PromptLibrarySyncRunEntity> LatestRunAsync() =>
            await Db.Queryable<PromptLibrarySyncRunEntity>()
                .OrderByDescending(x => x.Id)
                .FirstAsync();

        public async Task<List<PromptLibraryItemEntity>> ActiveItemsAsync() =>
            await Db.Queryable<PromptLibraryItemEntity>()
                .Where(x => x.Source == Source && x.IsActive)
                .OrderBy(x => x.SourcePosition)
                .ToListAsync();

        public PromptLibrarySyncRunEntity SeedSuccessfulRun(string contentHash)
        {
            var run = new PromptLibrarySyncRunEntity
            {
                Source = Source,
                SourceContentHash = contentHash,
                Status = PromptLibrarySyncStatuses.Succeeded,
                ParsedCount = 2,
                SelectedCount = 2,
                StartedAt = DateTime.Now.AddMinutes(-1),
                FinishedAt = DateTime.Now
            };
            run.Id = Db.Insertable(run).ExecuteReturnBigIdentity();
            return run;
        }

        public IReadOnlyList<PromptLibraryItemEntity> SeedActiveItems(
            long snapshotId,
            string version,
            int count)
        {
            var items = Enumerable.Range(1, count)
                .Select(number => new PromptLibraryItemEntity
                {
                    Source = Source,
                    SourceKey = $"seed-key-{number}",
                    ExternalNo = number,
                    ExternalOccurrence = 1,
                    Title = $"{version}-title-{number}",
                    Description = $"{version}-description-{number}",
                    PromptText = $"{version}-prompt-{number}",
                    PromptHash = new string((char)('a' + number - 1), 64),
                    CoverSourceUrl = $"https://cms-assets.youmind.com/{version}-{number}.webp",
                    CoverLocalPath = $"stored/{version}-{number}.webp",
                    SourceUrl = $"https://youmind.example/prompts/{number}",
                    Language = "zh-CN",
                    SourcePosition = number,
                    SnapshotId = snapshotId,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                })
                .ToArray();
            foreach (var item in items)
            {
                item.Id = Db.Insertable(item).ExecuteReturnBigIdentity();
            }
            return items;
        }

        public void ActivateSnapshot(long snapshotId)
        {
            var versions = Db.Queryable<PromptLibraryItemVersionEntity>()
                .Where(x => x.SnapshotId == snapshotId)
                .OrderBy(x => x.SourcePosition)
                .ToList();
            Assert.NotEmpty(versions);
            Db.Updateable<PromptLibraryItemEntity>()
                .SetColumns(x => new PromptLibraryItemEntity { IsActive = false })
                .Where(x => x.Source == Source && x.IsActive)
                .ExecuteCommand();
            foreach (var version in versions)
            {
                var affected = Db.Updateable<PromptLibraryItemEntity>()
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
                        UpdatedAt = DateTime.Now
                    })
                    .Where(x => x.Id == version.PromptId && x.Source == Source)
                    .ExecuteCommand();
                Assert.Equal(1, affected);
            }
        }

        public void SeedVersion(long snapshotId, long promptId, string version, DateTime createdAt)
        {
            Db.Insertable(new PromptLibraryItemVersionEntity
            {
                SnapshotId = snapshotId,
                PromptId = promptId,
                ExternalNo = 1,
                ExternalOccurrence = 1,
                Title = $"{version}-title-1",
                Description = $"{version}-description-1",
                PromptText = $"{version}-prompt-1",
                PromptHash = new string(char.ToLowerInvariant(version[0]), 64),
                CoverSourceUrl = $"https://cms-assets.youmind.com/{version}-1.webp",
                CoverLocalPath = $"stored/{version}-1.webp",
                SourceUrl = "https://youmind.example/prompts/1",
                Language = "zh-CN",
                SourcePosition = 1,
                CreatedAt = createdAt
            }).ExecuteCommand();
        }

        public void Dispose() => Db.Dispose();

        private void CreateTables()
        {
            Db.Ado.ExecuteCommand("""
                CREATE TABLE prompt_library_sync_run (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    source TEXT NOT NULL,
                    source_commit_sha TEXT NULL,
                    source_etag TEXT NULL,
                    source_readme_hash TEXT NULL,
                    status TEXT NOT NULL,
                    parsed_count INTEGER NOT NULL DEFAULT 0,
                    selected_count INTEGER NOT NULL DEFAULT 0,
                    downloaded_count INTEGER NOT NULL DEFAULT 0,
                    reused_image_count INTEGER NOT NULL DEFAULT 0,
                    failed_image_count INTEGER NOT NULL DEFAULT 0,
                    started_at TEXT NOT NULL,
                    finished_at TEXT NULL,
                    error_message TEXT NULL,
                    warning_message TEXT NULL
                )
                """);
            Db.Ado.ExecuteCommand("""
                CREATE TABLE prompt_library_item (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    source TEXT NOT NULL,
                    source_key TEXT NOT NULL,
                    external_no INTEGER NOT NULL,
                    external_occurrence INTEGER NOT NULL,
                    title TEXT NOT NULL,
                    description TEXT NOT NULL,
                    prompt_text TEXT NOT NULL,
                    prompt_hash TEXT NOT NULL,
                    cover_source_url TEXT NOT NULL,
                    cover_local_path TEXT NOT NULL,
                    author_name TEXT NULL,
                    author_url TEXT NULL,
                    source_url TEXT NULL,
                    source_published_at TEXT NULL,
                    language TEXT NULL,
                    source_position INTEGER NOT NULL,
                    snapshot_id INTEGER NOT NULL,
                    is_active INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                )
                """);
            Db.Ado.ExecuteCommand("""
                CREATE TABLE prompt_library_item_version (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    snapshot_id INTEGER NOT NULL,
                    prompt_id INTEGER NOT NULL,
                    external_no INTEGER NOT NULL,
                    external_occurrence INTEGER NOT NULL,
                    title TEXT NOT NULL,
                    description TEXT NOT NULL,
                    prompt_text TEXT NOT NULL,
                    prompt_hash TEXT NOT NULL,
                    cover_source_url TEXT NOT NULL,
                    cover_local_path TEXT NOT NULL,
                    author_name TEXT NULL,
                    author_url TEXT NULL,
                    source_url TEXT NULL,
                    source_published_at TEXT NULL,
                    language TEXT NULL,
                    source_position INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                )
                """);
        }
    }
}
