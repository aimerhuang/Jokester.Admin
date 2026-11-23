using System.Net;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Tests;

public sealed class AiImageModelStatusTests
{
    private static readonly DateTime BaseHongKong = new(2026, 11, 22, 10, 0, 0, DateTimeKind.Unspecified);

    [Theory]
    [InlineData(0, null, AiImageModelStatusStates.Unknown)]
    [InlineData(4, null, AiImageModelStatusStates.Unknown)]
    [InlineData(5, 100, AiImageModelStatusStates.Normal)]
    public void BuildItem_RequiresMinimumSamplesBeforeJudging(int sampleCount, int? availability, string state)
    {
        var item = AiImageModelStatusService.BuildItem("m", "M", Samples(sampleCount, sampleCount), new AiImageModelStatusOptions());

        Assert.Equal(state, item.State);
        Assert.Equal(availability, item.Availability);
        Assert.Equal(sampleCount, item.SampleCount);
        Assert.Equal(sampleCount, item.Recent.Count);
        if (availability is null)
        {
            Assert.Null(item.FastestSuccessSeconds);
            Assert.Null(item.SlowestSuccessSeconds);
        }
    }

    [Theory]
    [InlineData(90, AiImageModelStatusStates.Normal)]
    [InlineData(89, AiImageModelStatusStates.Degraded)]
    [InlineData(70, AiImageModelStatusStates.Degraded)]
    [InlineData(69, AiImageModelStatusStates.Down)]
    public void BuildItem_AppliesThresholds(int successes, string state)
    {
        var options = new AiImageModelStatusOptions { SampleLimit = 100 };

        var item = AiImageModelStatusService.BuildItem("m", "M", Samples(100, successes), options);

        Assert.Equal(successes, item.Availability);
        Assert.Equal(state, item.State);
    }

    [Fact]
    public void BuildItem_OrdersOldestToNewestAndMeasuresSuccessDurations()
    {
        var newestFirst = new[]
        {
            new AiImageModelHealthSample(false, BaseHongKong.AddMinutes(4), BaseHongKong.AddMinutes(5)),
            new AiImageModelHealthSample(true, BaseHongKong.AddMinutes(3), BaseHongKong.AddMinutes(3).AddSeconds(66)),
            new AiImageModelHealthSample(true, BaseHongKong.AddMinutes(2), BaseHongKong.AddMinutes(2).AddSeconds(18)),
            new AiImageModelHealthSample(false, BaseHongKong.AddMinutes(1), BaseHongKong.AddMinutes(1).AddSeconds(5)),
            new AiImageModelHealthSample(true, BaseHongKong, BaseHongKong.AddSeconds(30))
        };

        var item = AiImageModelStatusService.BuildItem("m", "M", newestFirst, new AiImageModelStatusOptions());

        Assert.Equal([1, 0, 1, 1, 0], item.Recent);
        Assert.Equal(60, item.Availability);
        Assert.Equal(AiImageModelStatusStates.Down, item.State);
        Assert.Equal(18, item.FastestSuccessSeconds);
        Assert.Equal(66, item.SlowestSuccessSeconds);
        // Storage is Hong Kong time; the API exposes UTC.
        Assert.Equal(new DateTime(2026, 11, 22, 2, 0, 30, DateTimeKind.Utc), item.RecentCompletedAt[0]);
        Assert.Equal(DateTimeKind.Utc, item.LastSampleAt!.Value.Kind);
        Assert.Equal(new DateTime(2026, 11, 22, 2, 5, 0, DateTimeKind.Utc), item.LastSampleAt);
    }

    [Fact]
    public void BuildItem_KeepsOnlyTheNewestSampleLimit()
    {
        var newestFirst = Enumerable.Range(0, 80)
            .Select(i => new AiImageModelHealthSample(i < 60, BaseHongKong.AddMinutes(-i), BaseHongKong.AddMinutes(-i).AddSeconds(20)))
            .ToArray();

        var item = AiImageModelStatusService.BuildItem("m", "M", newestFirst, new AiImageModelStatusOptions());

        Assert.Equal(60, item.SampleCount);
        Assert.Equal(100, item.Availability);
    }

    [Fact]
    public async Task ComputeAsync_UsesHealthColumnsIncludesSoftDeletedAndRespectsWindow()
    {
        using var db = CreateTaskDb();
        var utcNow = new DateTime(2026, 11, 22, 2, 0, 0, DateTimeKind.Utc);
        var hongKongNow = utcNow.AddHours(8);
        var id = 0;
        void Add(string? code, int? sample, DateTime completedAt, bool deleted = false) =>
            db.Ado.ExecuteCommand(
                "INSERT INTO ai_image_task (id, health_model_code, health_sample, created_at, completed_at, is_deleted) VALUES (@id, @code, @sample, @created, @completed, @deleted)",
                new
                {
                    id = ++id,
                    code,
                    sample,
                    created = completedAt.AddSeconds(-40),
                    completed = completedAt,
                    deleted = deleted ? 1 : 0
                });

        for (var i = 0; i < 4; i++) Add("gpt-image-2", AiImageModelHealth.Success, hongKongNow.AddMinutes(-i - 1));
        Add("gpt-image-2", AiImageModelHealth.Failure, hongKongNow.AddMinutes(-10), deleted: true);
        Add("gpt-image-2", AiImageModelHealth.Failure, hongKongNow.AddHours(-25));
        Add(null, null, hongKongNow.AddMinutes(-2));
        Add("nano-banana-2", AiImageModelHealth.Failure, hongKongNow.AddMinutes(-3));

        var configService = new Mock<IAiImageModelConfigService>();
        configService
            .Setup(x => x.GetEnabledModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AiImageModelOptionDto { Code = "gpt-image-2", Name = "GPT Image 2", Resolutions = ["1k"] },
                new AiImageModelOptionDto { Code = "nano-banana-2", Name = "Nano", Resolutions = ["1k"] },
                new AiImageModelOptionDto { Code = "unpriced", Name = "Unpriced", Resolutions = [] }
            ]);

        var response = await AiImageModelStatusService.ComputeAsync(db, configService.Object, new AiImageModelStatusOptions(), utcNow, default);

        Assert.Equal(["gpt-image-2", "nano-banana-2"], response.Items.Select(x => x.ModelCode));
        var gpt = response.Items[0];
        Assert.Equal(5, gpt.SampleCount);
        Assert.Equal([0, 1, 1, 1, 1], gpt.Recent);
        Assert.Equal(80, gpt.Availability);
        Assert.Equal(AiImageModelStatusStates.Degraded, gpt.State);
        Assert.Equal(40, gpt.FastestSuccessSeconds);
        var nano = response.Items[1];
        Assert.Equal(1, nano.SampleCount);
        Assert.Equal(AiImageModelStatusStates.Unknown, nano.State);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(29.9, true)]
    public async Task RefreshAsync_AdoptsSharedSnapshotOnlyWhileYoungerThanHalfTheInterval(double ageSeconds, bool expectRecompute)
    {
        var shared = new AiImageModelStatusResponse
        {
            GeneratedAt = DateTime.UtcNow.AddSeconds(-ageSeconds),
            WindowHours = 24,
            SampleLimit = 60,
            Items = []
        };
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(System.Text.Json.JsonSerializer.Serialize(shared, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))));
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(database.Object);
        var configService = new Mock<IAiImageModelConfigService>();
        configService.Setup(x => x.GetEnabledModelsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var services = new ServiceCollection()
            .AddScoped(_ => Mock.Of<ISqlSugarClient>())
            .AddScoped(_ => configService.Object)
            .BuildServiceProvider();
        var service = new AiImageModelStatusService(
            services.GetRequiredService<IServiceScopeFactory>(),
            redis.Object,
            Options.Create(new RedisOptions()),
            Options.Create(new AiImageModelStatusOptions { RefreshSeconds = 30 }),
            NullLogger<AiImageModelStatusService>.Instance);

        await service.RefreshAsync(default);

        configService.Verify(x => x.GetEnabledModelsAsync(It.IsAny<CancellationToken>()), expectRecompute ? Times.Once() : Times.Never());
    }

    [Fact]
    public void LegacyFailureClassification_ExcludesUserAndPlatformSideFailures()
    {
        Assert.False(AiImageModelHealth.IsModelSideFailure(new AiPromptFilterUnavailableException()));
        Assert.False(AiImageModelHealth.IsModelSideFailure(
            new AppException(ErrorCodes.PreconditionFailed, MachineErrorCodes.AiConsentRequired, "consent")));
        Assert.False(AiImageModelHealth.IsModelSideFailure(
            new AppException(ErrorCodes.BadRequest, MachineErrorCodes.InputAssetInvalid, "asset")));
        Assert.False(AiImageModelHealth.IsModelSideFailure(new NotFoundException("missing reference")));

        Assert.True(AiImageModelHealth.IsModelSideFailure(new HttpRequestException("502")));
        Assert.True(AiImageModelHealth.IsModelSideFailure(new TimeoutException()));
        Assert.True(AiImageModelHealth.IsModelSideFailure(new AppException(ErrorCodes.BadRequest, "route API key is not configured")));
    }

    [Theory]
    [InlineData(1, null, AiImageModelHealth.Success)]
    [InlineData(2, "provider", AiImageModelHealth.Failure)]
    [InlineData(2, "output", AiImageModelHealth.Failure)]
    [InlineData(2, "preflight", null)]
    [InlineData(2, "settlement", null)]
    [InlineData(2, null, null)]
    public void VersionedClassification_CountsOnlyProviderAndOutputStages(int finalStatus, string? stage, int? expected)
    {
        Assert.Equal(expected, AiImageModelHealth.ForVersioned(finalStatus, stage));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("2606:4700::1111", false)]
    public void EndpointPolicy_BlocksPrivateAndReservedAddresses(string address, bool blocked)
    {
        Assert.Equal(blocked, AiImageRouteEndpointPolicy.IsBlockedAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("/images/generations", true)]
    [InlineData("/v1beta/models/{model}:generateContent", true)]
    [InlineData("images/generations", false)]
    [InlineData("//evil.example/x", false)]
    [InlineData("/images/../admin", false)]
    [InlineData("/images?x=1", false)]
    [InlineData("https://evil.example/images", false)]
    public void EndpointPolicy_ValidatesPaths(string path, bool valid)
    {
        if (valid)
        {
            Assert.Equal(path, AiImageRouteEndpointPolicy.NormalizePath(path, "/default"));
        }
        else
        {
            Assert.Throws<AppException>(() => AiImageRouteEndpointPolicy.NormalizePath(path, "/default"));
        }
    }

    private static AiImageModelHealthSample[] Samples(int count, int successes) =>
        Enumerable.Range(0, count)
            .Select(i => new AiImageModelHealthSample(
                i < successes,
                BaseHongKong.AddMinutes(-i),
                BaseHongKong.AddMinutes(-i).AddSeconds(30)))
            .ToArray();

    private static SqlSugarClient CreateTaskDb()
    {
        SQLitePCL.Batteries_V2.Init();
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute
        });
        db.Ado.ExecuteCommand("""
            CREATE TABLE ai_image_task (
                id INTEGER PRIMARY KEY,
                health_model_code TEXT NULL,
                health_sample INTEGER NULL,
                created_at TEXT NOT NULL,
                completed_at TEXT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0
            );
            """);
        return db;
    }
}
