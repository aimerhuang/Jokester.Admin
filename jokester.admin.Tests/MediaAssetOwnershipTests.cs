using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class MediaAssetOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "jokester-media-ownership-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AssetOwnedByAnotherUser_IsHiddenLikeMissingAsset()
    {
        using var context = CreateContext();
        var otherUsersAsset = "AST20260812000000000000000000000001";
        Directory.CreateDirectory(Path.Combine(_root, "2"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "2", "secret.png"), [1, 2, 3]);
        context.Db.Insertable(new MediaAssetEntity
        {
            AssetId = otherUsersAsset,
            OwnerUserId = 2,
            AssetType = "reference",
            StorageKey = "2/secret.png",
            MimeType = "image/png",
            Width = 1,
            Height = 1,
            SizeBytes = 3,
            Sha256 = new string('A', 64),
            MetadataStripped = true,
            CreatedAt = DateTime.UtcNow
        }).ExecuteCommand();

        var forbidden = await Assert.ThrowsAsync<AppException>(() =>
            context.Service.ResolveOwnedReferenceUrlsAsync(
                1,
                false,
                [otherUsersAsset],
                null,
                default));
        var missing = await Assert.ThrowsAsync<AppException>(() =>
            context.Service.ResolveOwnedReferenceUrlsAsync(
                1,
                false,
                ["AST20260812000000000000000000000002"],
                null,
                default));

        Assert.Equal(ErrorCodes.NotFound, forbidden.Code);
        Assert.Equal(MachineErrorCodes.ResourceNotFound, forbidden.MachineCode);
        Assert.Equal(missing.Code, forbidden.Code);
        Assert.Equal(missing.MachineCode, forbidden.MachineCode);
        Assert.Null(await context.Service.GetContentAsync(
            otherUsersAsset,
            1,
            false,
            false,
            default));
    }

    [Theory]
    [InlineData("/api/media/ai/1/../2/secret.png")]
    [InlineData("/api/media/ai/1\\..\\2\\secret.png")]
    public async Task LegacyUrl_CannotTraverseIntoAnotherUsersDirectory(string url)
    {
        using var context = CreateContext();
        Directory.CreateDirectory(Path.Combine(_root, "2"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "2", "secret.png"), [1, 2, 3]);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            context.Service.ResolveOwnedReferenceUrlsAsync(1, false, null, [url], default));

        Assert.Equal(ErrorCodes.NotFound, exception.Code);
        Assert.Equal(MachineErrorCodes.ResourceNotFound, exception.MachineCode);
    }

    [Fact]
    public async Task DeleteOwnedAsset_SoftDeletesRecordAndRemovesStoredFiles_Idempotently()
    {
        using var context = CreateContext();
        const string assetId = "AST20260812000000000000000000000003";
        var storagePath = Path.Combine(_root, "1", "asset.png");
        var thumbnailPath = Path.Combine(_root, "1", "asset_thumb.webp");
        Directory.CreateDirectory(Path.GetDirectoryName(storagePath)!);
        await File.WriteAllBytesAsync(storagePath, [1, 2, 3]);
        await File.WriteAllBytesAsync(thumbnailPath, [4, 5, 6]);
        InsertAsset(context.Db, assetId, 1, "1/asset.png", "1/asset_thumb.webp");

        await context.Service.DeleteOwnedAsync(assetId, 1, default);
        await context.Service.DeleteOwnedAsync(assetId, 1, default);

        var persisted = context.Db.Queryable<MediaAssetEntity>().Single(x => x.AssetId == assetId);
        Assert.True(persisted.IsDeleted);
        Assert.NotNull(persisted.DeletedAt);
        Assert.False(File.Exists(storagePath));
        Assert.False(File.Exists(thumbnailPath));
        Assert.Null(await context.Service.GetContentAsync(assetId, 1, false, false, default));
    }

    [Fact]
    public async Task DeleteAssetOwnedByAnotherUser_IsHiddenAndDoesNotChangeAsset()
    {
        using var context = CreateContext();
        const string assetId = "AST20260812000000000000000000000004";
        var storagePath = Path.Combine(_root, "2", "asset.png");
        Directory.CreateDirectory(Path.GetDirectoryName(storagePath)!);
        await File.WriteAllBytesAsync(storagePath, [1, 2, 3]);
        InsertAsset(context.Db, assetId, 2, "2/asset.png");

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            context.Service.DeleteOwnedAsync(assetId, 1, default));

        Assert.Equal(ErrorCodes.NotFound, exception.Code);
        Assert.Equal(MachineErrorCodes.ResourceNotFound, exception.MachineCode);
        Assert.False(context.Db.Queryable<MediaAssetEntity>().Single(x => x.AssetId == assetId).IsDeleted);
        Assert.True(File.Exists(storagePath));
    }

    private static void InsertAsset(
        ISqlSugarClient db,
        string assetId,
        long ownerUserId,
        string storageKey,
        string? thumbnailKey = null)
    {
        db.Insertable(new MediaAssetEntity
        {
            AssetId = assetId,
            OwnerUserId = ownerUserId,
            AssetType = "reference",
            StorageKey = storageKey,
            ThumbnailKey = thumbnailKey,
            MimeType = "image/png",
            Width = 1,
            Height = 1,
            SizeBytes = 3,
            Sha256 = new string('A', 64),
            MetadataStripped = true,
            CreatedAt = DateTime.UtcNow
        }).ExecuteCommand();
    }

    private TestContext CreateContext()
    {
        Directory.CreateDirectory(_root);
        SQLitePCL.Batteries_V2.Init();
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.Ado.ExecuteCommand("""
            CREATE TABLE media_asset (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id TEXT NOT NULL UNIQUE,
                owner_user_id INTEGER NOT NULL,
                asset_type TEXT NOT NULL,
                storage_key TEXT NOT NULL,
                thumbnail_key TEXT NULL,
                mime_type TEXT NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                size_bytes INTEGER NOT NULL,
                sha256 TEXT NOT NULL,
                metadata_stripped INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                deleted_at TEXT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0
            );
            """);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(_root);
        environment.SetupGet(x => x.ApplicationName).Returns("tests");
        environment.SetupGet(x => x.EnvironmentName).Returns("Development");
        environment.SetupGet(x => x.ContentRootFileProvider).Returns(new NullFileProvider());
        var resolver = new AiMediaPathResolver(
            Options.Create(new AiMediaStorageOptions { RootPath = _root }),
            environment.Object,
            NullLogger<AiMediaPathResolver>.Instance);
        return new TestContext(db, new MediaAssetService(db, resolver));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record TestContext(SqlSugarClient Db, MediaAssetService Service) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }
}
