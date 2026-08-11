using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Services;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class PromptLibrarySnapshotSwitchTests
{
    [Fact]
    public async Task SwitchSnapshotAsync_RestoresTargetVersionsAndDeactivatesOtherItems()
    {
        using var context = new SnapshotTestContext(targetCount: 2);
        var currentRun = context.SeedRun(PromptLibrarySyncStatuses.Succeeded);
        var targetRun = context.SeedRun(PromptLibrarySyncStatuses.Succeeded);
        var first = context.SeedStableItem(currentRun.Id, 1, active: true);
        var second = context.SeedStableItem(currentRun.Id, 2, active: true);
        var unrelated = context.SeedStableItem(currentRun.Id, 3, active: true);
        context.SeedVersion(targetRun.Id, first.Id, 1);
        context.SeedVersion(targetRun.Id, second.Id, 2);

        var result = await context.Service.SwitchSnapshotAsync(targetRun.Id, CancellationToken.None);

        Assert.True(result.Changed);
        Assert.Equal(targetRun.Id, result.SnapshotId);
        Assert.Equal(currentRun.Id, result.PreviousSnapshotId);
        Assert.Equal(2, result.ActiveItemCount);

        var restored = await context.Db.Queryable<PromptLibraryItemEntity>()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SourcePosition)
            .ToListAsync();
        Assert.Collection(
            restored,
            item => AssertRestored(item, targetRun.Id, 1),
            item => AssertRestored(item, targetRun.Id, 2));
        Assert.False((await context.Db.Queryable<PromptLibraryItemEntity>()
            .FirstAsync(x => x.Id == unrelated.Id)).IsActive);
        context.Queue.Verify(x => x.EndSnapshotSwitch(), Times.Once);
    }

    [Fact]
    public async Task SwitchSnapshotAsync_ReturnsUnchangedWhenTargetIsAlreadyActive()
    {
        using var context = new SnapshotTestContext(targetCount: 2);
        var targetRun = context.SeedRun(PromptLibrarySyncStatuses.Succeeded);
        var first = context.SeedStableItem(targetRun.Id, 1, active: true);
        var second = context.SeedStableItem(targetRun.Id, 2, active: true);
        context.SeedVersion(targetRun.Id, first.Id, 1);
        context.SeedVersion(targetRun.Id, second.Id, 2);

        var result = await context.Service.SwitchSnapshotAsync(targetRun.Id, CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(targetRun.Id, result.PreviousSnapshotId);
        Assert.Equal(2, result.ActiveItemCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SwitchSnapshotAsync_RejectsQueuedOrRunningSynchronization(bool queued, bool running)
    {
        using var context = new SnapshotTestContext(targetCount: 1);
        context.Queue.SetupGet(x => x.IsQueued).Returns(queued);
        context.Queue.SetupGet(x => x.IsRunning).Returns(running);
        context.Queue.Setup(x => x.TryBeginSnapshotSwitch()).Returns(false);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            context.Service.SwitchSnapshotAsync(1, CancellationToken.None));

        Assert.Contains("queued or running", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwitchSnapshotAsync_RejectsIncompleteSnapshotWithoutChangingCurrentItems()
    {
        using var context = new SnapshotTestContext(targetCount: 2);
        var currentRun = context.SeedRun(PromptLibrarySyncStatuses.Succeeded);
        var targetRun = context.SeedRun(PromptLibrarySyncStatuses.Succeeded);
        var first = context.SeedStableItem(currentRun.Id, 1, active: true);
        context.SeedVersion(targetRun.Id, first.Id, 1);

        await Assert.ThrowsAsync<ConflictException>(() =>
            context.Service.SwitchSnapshotAsync(targetRun.Id, CancellationToken.None));

        var unchanged = await context.Db.Queryable<PromptLibraryItemEntity>().FirstAsync(x => x.Id == first.Id);
        Assert.True(unchanged.IsActive);
        Assert.Equal(currentRun.Id, unchanged.SnapshotId);
        context.Queue.Verify(x => x.EndSnapshotSwitch(), Times.Once);
        Assert.Equal("current-title-1", unchanged.Title);
    }

    [Fact]
    public async Task SwitchSnapshotAsync_RejectsMissingCoverWithoutChangingCurrentItems()
    {
        using var context = new SnapshotTestContext(targetCount: 1);
        var currentRun = context.SeedRun(PromptLibrarySyncStatuses.Succeeded);
        var targetRun = context.SeedRun(PromptLibrarySyncStatuses.Succeeded);
        var first = context.SeedStableItem(currentRun.Id, 1, active: true);
        context.SeedVersion(targetRun.Id, first.Id, 1);
        context.ImageStore
            .Setup(x => x.IsStoredImageAvailable("test/cover-1.png"))
            .Returns(false);

        await Assert.ThrowsAsync<ConflictException>(() =>
            context.Service.SwitchSnapshotAsync(targetRun.Id, CancellationToken.None));

        var unchanged = await context.Db.Queryable<PromptLibraryItemEntity>().FirstAsync(x => x.Id == first.Id);
        Assert.True(unchanged.IsActive);
        Assert.Equal(currentRun.Id, unchanged.SnapshotId);
    }

    [Fact]
    public async Task SwitchSnapshotAsync_RollsBackWhenAStableItemDisappearsDuringActivation()
    {
        using var context = new SnapshotTestContext(targetCount: 2);
        var currentRun = context.SeedRun(PromptLibrarySyncStatuses.Succeeded);
        var targetRun = context.SeedRun(PromptLibrarySyncStatuses.Succeeded);
        var first = context.SeedStableItem(currentRun.Id, 1, active: true);
        var second = context.SeedStableItem(currentRun.Id, 2, active: true);
        context.SeedVersion(targetRun.Id, first.Id, 1);
        context.SeedVersion(targetRun.Id, second.Id, 2);
        context.Db.Ado.ExecuteCommand($"""
            CREATE TRIGGER delete_second_item_after_deactivation
            AFTER UPDATE OF is_active ON prompt_library_item
            WHEN OLD.id = {first.Id} AND NEW.is_active = 0
            BEGIN
                DELETE FROM prompt_library_item WHERE id = {second.Id};
            END;
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.SwitchSnapshotAsync(targetRun.Id, CancellationToken.None));

        var rolledBack = await context.Db.Queryable<PromptLibraryItemEntity>().FirstAsync(x => x.Id == first.Id);
        Assert.True(rolledBack.IsActive);
        Assert.Equal(currentRun.Id, rolledBack.SnapshotId);
        Assert.Equal("current-title-1", rolledBack.Title);
    }

    private static void AssertRestored(PromptLibraryItemEntity item, long snapshotId, int number)
    {
        Assert.Equal(snapshotId, item.SnapshotId);
        Assert.Equal($"restored-title-{number}", item.Title);
        Assert.Equal($"restored-description-{number}", item.Description);
        Assert.Equal($"restored-prompt-{number}", item.PromptText);
        Assert.Equal($"test/cover-{number}.png", item.CoverLocalPath);
        Assert.Equal("zh-CN", item.Language);
        Assert.True(item.IsActive);
    }

    private sealed class SnapshotTestContext : IDisposable
    {
        private const string Source = "test-source";
        private readonly ServiceProvider serviceProvider;

        public SnapshotTestContext(int targetCount)
        {
            SQLitePCL.Batteries_V2.Init();
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=:memory:",
                DbType = SqlSugar.DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute
            });
            CreateSchema(Db);

            Queue = new Mock<IPromptLibrarySyncQueue>();
            Queue.Setup(x => x.TryBeginSnapshotSwitch()).Returns(true);
            ImageStore = new Mock<IPromptLibraryImageStore>();
            ImageStore.Setup(x => x.IsStoredImageAvailable(It.IsAny<string?>())).Returns(true);
            serviceProvider = new ServiceCollection()
                .AddSingleton(ImageStore.Object)
                .BuildServiceProvider();
            Service = new PromptLibrarySyncAdminService(
                Db,
                Queue.Object,
                Options.Create(new PromptLibraryOptions
                {
                    Enabled = true,
                    Source = Source,
                    TargetCount = targetCount,
                    ImageRoot = Path.GetTempPath()
                }),
                serviceProvider,
                NullLogger<PromptLibrarySyncAdminService>.Instance);
        }

        public SqlSugarClient Db { get; }

        public Mock<IPromptLibrarySyncQueue> Queue { get; }

        public Mock<IPromptLibraryImageStore> ImageStore { get; }

        public PromptLibrarySyncAdminService Service { get; }

        public PromptLibrarySyncRunEntity SeedRun(string status, string source = Source)
        {
            var run = new PromptLibrarySyncRunEntity
            {
                Source = source,
                Status = status,
                StartedAt = DateTime.Now,
                FinishedAt = DateTime.Now
            };
            run.Id = Db.Insertable(run).ExecuteReturnBigIdentity();
            return run;
        }

        public PromptLibraryItemEntity SeedStableItem(long snapshotId, int number, bool active)
        {
            var item = new PromptLibraryItemEntity
            {
                Source = Source,
                SourceKey = $"source-key-{number}",
                ExternalNo = number,
                ExternalOccurrence = 1,
                Title = $"current-title-{number}",
                Description = $"current-description-{number}",
                PromptText = $"current-prompt-{number}",
                PromptHash = $"current-hash-{number}",
                CoverSourceUrl = $"https://example.com/current-{number}.png",
                CoverLocalPath = $"test/current-{number}.png",
                Language = "en",
                SourcePosition = number,
                SnapshotId = snapshotId,
                IsActive = active,
                CreatedAt = DateTime.Now
            };
            item.Id = Db.Insertable(item).ExecuteReturnBigIdentity();
            return item;
        }

        public void SeedVersion(long snapshotId, long promptId, int number)
        {
            Db.Insertable(new PromptLibraryItemVersionEntity
            {
                SnapshotId = snapshotId,
                PromptId = promptId,
                ExternalNo = number + 100,
                ExternalOccurrence = 1,
                Title = $"restored-title-{number}",
                Description = $"restored-description-{number}",
                PromptText = $"restored-prompt-{number}",
                PromptHash = $"restored-hash-{number}",
                CoverSourceUrl = $"https://example.com/cover-{number}.png",
                CoverLocalPath = $"test/cover-{number}.png",
                AuthorName = $"author-{number}",
                SourceUrl = $"https://example.com/prompts/{number}",
                Language = "zh-CN",
                SourcePosition = number,
                CreatedAt = DateTime.Now
            }).ExecuteCommand();
        }

        public void Dispose()
        {
            serviceProvider.Dispose();
            Db.Dispose();
        }

        private static void CreateSchema(ISqlSugarClient db)
        {
            db.Ado.ExecuteCommand("""
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
                );
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
                );
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
                );
                """);
        }
    }
}
