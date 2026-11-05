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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiResultAssetRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "jokester-ai-result-asset-tests",
        Guid.NewGuid().ToString("N"));
    private readonly SqlSugarClient _db;
    private readonly MediaAssetService _service;

    public AiResultAssetRegistrationTests()
    {
        Directory.CreateDirectory(_root);
        SQLitePCL.Batteries_V2.Init();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        AiCanvasServiceTests.CreateSchema(_db);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(_root);
        environment.SetupGet(x => x.ApplicationName).Returns("tests");
        environment.SetupGet(x => x.EnvironmentName).Returns("Development");
        environment.SetupGet(x => x.ContentRootFileProvider).Returns(new NullFileProvider());
        var resolver = new AiMediaPathResolver(
            Options.Create(new AiMediaStorageOptions { RootPath = _root }),
            environment.Object,
            NullLogger<AiMediaPathResolver>.Instance);
        _service = new MediaAssetService(_db, resolver);
    }

    [Fact]
    public async Task VersionedResult_IsRegisteredOnceAndReusesOriginalFile()
    {
        var url = await WriteResultImageAsync(1, "result.png", 64, 32);
        InsertTask(10, 1, status: 1);
        InsertResult(10, 0, url);

        var first = await _service.RegisterAiResultAsync(1, 10, 0, default);
        var repeated = await _service.RegisterAiResultAsync(1, 10, 0, default);

        Assert.Equal(first.AssetId, repeated.AssetId);
        Assert.Equal(64, first.Width);
        Assert.Equal(32, first.Height);
        Assert.Equal("image/png", first.MimeType);
        var asset = _db.Queryable<MediaAssetEntity>().Single();
        Assert.Equal("generated", asset.AssetType);
        Assert.Equal("1/ai/result.png", asset.StorageKey);
        Assert.Equal(10, asset.SourceTaskId);
        Assert.Equal(0, asset.SourceResultOrdinal);
        Assert.True(File.Exists(Path.Combine(_root, asset.ThumbnailKey!)));
        Assert.NotNull(await _service.GetContentAsync(first.AssetId, 1, false, false, default));
    }

    [Fact]
    public async Task LegacyResultUrls_AreResolvedByOrdinal()
    {
        var firstUrl = await WriteResultImageAsync(1, "legacy-0.png", 8, 8);
        var secondUrl = await WriteResultImageAsync(1, "legacy-1.png", 16, 8);
        InsertTask(11, 1, status: 1, resultUrls: $"[\"{firstUrl}\",\"{secondUrl}\"]");

        var registered = await _service.RegisterAiResultAsync(1, 11, 1, default);

        Assert.Equal(16, registered.Width);
        Assert.Equal("1/ai/legacy-1.png", _db.Queryable<MediaAssetEntity>().Single().StorageKey);
    }

    [Fact]
    public async Task UnavailableResults_AreHiddenAsNotFound()
    {
        var ownUrl = await WriteResultImageAsync(1, "own.png", 8, 8);
        var otherUrl = await WriteResultImageAsync(2, "other.png", 8, 8);
        InsertTask(20, 2, status: 1);
        InsertResult(20, 0, otherUrl);
        InsertTask(21, 1, status: 2);
        InsertResult(21, 0, ownUrl);
        InsertTask(22, 1, status: 1);
        InsertResult(22, 0, ownUrl, quarantined: true);
        InsertTask(23, 1, status: 1, isDeleted: true);
        InsertResult(23, 0, ownUrl);
        InsertTask(24, 1, status: 1, resultUrls: $"[\"{otherUrl}\"]");
        InsertTask(25, 1, status: 1);
        InsertResult(25, 0, ownUrl);

        var cases = new (long TaskId, int Ordinal)[] { (20, 0), (21, 0), (22, 0), (23, 0), (24, 0), (25, 1), (999, 0) };
        foreach (var (taskId, ordinal) in cases)
        {
            var exception = await Assert.ThrowsAsync<AppException>(() => _service.RegisterAiResultAsync(1, taskId, ordinal, default));
            Assert.Equal(MachineErrorCodes.ResourceNotFound, exception.MachineCode);
        }
        Assert.Empty(_db.Queryable<MediaAssetEntity>().ToList());
    }

    [Fact]
    public async Task DeletingGeneratedAsset_KeepsOriginalAndReRegisterRestoresSameAsset()
    {
        var url = await WriteResultImageAsync(1, "kept.png", 8, 8);
        InsertTask(30, 1, status: 1);
        InsertResult(30, 0, url);
        var registered = await _service.RegisterAiResultAsync(1, 30, 0, default);
        var thumbnailPath = Path.Combine(_root, _db.Queryable<MediaAssetEntity>().Single().ThumbnailKey!);

        await _service.DeleteOwnedAsync(registered.AssetId, 1, default);

        Assert.True(File.Exists(Path.Combine(_root, "1", "ai", "kept.png")));
        Assert.False(File.Exists(thumbnailPath));
        Assert.True(_db.Queryable<MediaAssetEntity>().Single().IsDeleted);

        var restored = await _service.RegisterAiResultAsync(1, 30, 0, default);

        Assert.Equal(registered.AssetId, restored.AssetId);
        Assert.False(_db.Queryable<MediaAssetEntity>().Single().IsDeleted);
        Assert.True(File.Exists(thumbnailPath));
    }

    [Fact]
    public async Task AssetRegisteredByAnotherUser_IsHidden()
    {
        var url = await WriteResultImageAsync(2, "theirs.png", 8, 8);
        InsertTask(40, 2, status: 1);
        InsertResult(40, 0, url);
        await _service.RegisterAiResultAsync(2, 40, 0, default);

        var exception = await Assert.ThrowsAsync<AppException>(() => _service.RegisterAiResultAsync(1, 40, 0, default));

        Assert.Equal(ErrorCodes.NotFound, exception.Code);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<string> WriteResultImageAsync(long userId, string fileName, int width, int height)
    {
        var directory = Path.Combine(_root, userId.ToString(), "ai");
        Directory.CreateDirectory(directory);
        using var image = new Image<Rgba32>(width, height);
        await image.SaveAsPngAsync(Path.Combine(directory, fileName));
        return $"/api/media/ai/{userId}/ai/{fileName}";
    }

    private void InsertTask(long id, long userId, int status, string? resultUrls = null, bool isDeleted = false) =>
        _db.Ado.ExecuteCommand(
            "INSERT INTO ai_image_task (id, user_id, status, result_urls, is_deleted) VALUES (@id, @userId, @status, @resultUrls, @isDeleted)",
            new { id, userId, status, resultUrls, isDeleted = isDeleted ? 1 : 0 });

    private void InsertResult(long taskId, int ordinal, string url, bool quarantined = false) =>
        _db.Insertable(new AiImageTaskResultEntity
        {
            TaskId = taskId,
            ResultOrdinal = ordinal,
            Url = url,
            Width = 1,
            Height = 1,
            Size = "1x1",
            MimeType = "image/png",
            IsQuarantined = quarantined,
            CreatedAt = DateTime.UtcNow
        }).ExecuteCommand();
}
