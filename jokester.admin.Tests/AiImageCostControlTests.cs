using jokester.admin.Application.Security;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace jokester.admin.Tests;

public sealed class AiImageCostControlTests
{
    [Fact]
    public void IdempotencyIdentity_IsStableAndPayloadSensitive()
    {
        var first = AiImageIdempotency.Create(" request-0001 ", new { Prompt = "cat", Count = 1 });
        var repeated = AiImageIdempotency.Create("request-0001", new { Prompt = "cat", Count = 1 });
        var changed = AiImageIdempotency.Create("request-0001", new { Prompt = "dog", Count = 1 });

        Assert.Equal(first, repeated);
        Assert.NotEqual(first.RequestFingerprint, changed.RequestFingerprint);
        Assert.Equal(64, first.KeyHash.Length);
        Assert.Throws<AppException>(() => AiImageIdempotency.Create("short", new { Prompt = "cat" }));
    }

    [Fact]
    public void IdempotencyTaskKeys_AreStableAndUniquePerImage()
    {
        var identity = AiImageIdempotency.Create("request-0002", new { Prompt = "cat", Count = 4 });
        var taskKeys = Enumerable.Range(0, 4)
            .Select(index => AiImageIdempotency.DeriveTaskKeyHash(identity.KeyHash, index))
            .ToArray();

        Assert.Equal(identity.KeyHash, taskKeys[0]);
        Assert.Equal(4, taskKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(taskKeys, key => Assert.Equal(64, key.Length));
        Assert.Equal(taskKeys[3], AiImageIdempotency.DeriveTaskKeyHash(identity.KeyHash, 3));
    }

    [Fact]
    public async Task TaskQueue_TracksBacklogAcrossEnqueueAndDequeue()
    {
        var queue = new AiImageTaskQueue();
        Assert.True(queue.TryQueue(42));
        Assert.True(queue.TryQueue(42));
        Assert.Equal(1, queue.BacklogCount);

        await using var enumerator = queue.DequeueAllAsync(default).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(42, enumerator.Current);
        Assert.Equal(0, queue.BacklogCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RedisAdmission_IsIdempotentAndRestoresFailedQuota_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("JOKESTER_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var configuration = ConfigurationOptions.Parse(connectionString);
        configuration.AbortOnConnectFail = true;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(configuration);
        var instanceName = $"jokester-ai-cost-test:{Guid.NewGuid():N}";
        var options = new AiCostControlOptions
        {
            DailyImageLimitPerUser = 2,
            DailyPointLimitPerUser = 100,
            MaxConcurrentTasksPerUser = 2,
            MaxQueuedTasks = 2,
            MaxGlobalProviderConcurrency = 1,
            IdempotencyTtlHours = 1,
            ReservationTtlMinutes = 5,
            ProviderLeaseSeconds = 30,
            ProviderFailureThreshold = 2,
            ProviderFailureWindowSeconds = 30,
            ProviderCircuitOpenSeconds = 30
        };
        var service = new AiImageAdmissionService(
            connection,
            Options.Create(new RedisOptions { InstanceName = instanceName, ConnectionString = connectionString }),
            Options.Create(options),
            new AiImageTaskQueue(),
            NullLogger<AiImageAdmissionService>.Instance);

        try
        {
            var reservations = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => service.ReserveAsync(42, new string('a', 64), new string('b', 64), 2, 100, default)));

            Assert.Single(reservations, x => !x.IsDuplicate);
            Assert.Equal(7, reservations.Count(x => x.IsDuplicate));

            var owner = reservations.Single(x => !x.IsDuplicate);
            await service.BindTaskAsync(owner, 99, default);
            var repeated = await service.ReserveAsync(42, new string('a', 64), new string('b', 64), 2, 100, default);
            Assert.True(repeated.IsDuplicate);
            Assert.Equal(99, repeated.ExistingTaskId);

            var concurrent = await Assert.ThrowsAsync<AppException>(() =>
                service.ReserveAsync(42, new string('d', 64), new string('e', 64), 1, 10, default));
            Assert.Equal(ErrorCodes.TooManyRequests, concurrent.Code);

            var backlog = await Assert.ThrowsAsync<AppException>(() =>
                service.ReserveAsync(43, new string('f', 64), new string('0', 64), 1, 10, default));
            Assert.Equal(ErrorCodes.ServiceUnavailable, backlog.Code);

            var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
                service.ReserveAsync(42, new string('a', 64), new string('c', 64), 2, 100, default));
            Assert.Equal(ErrorCodes.Conflict, conflict.Code);

            await service.CompleteAsync(
                new AiImageTaskEntity
                {
                    Id = 99,
                    UserId = 42,
                    ImageCount = 2,
                    PointCost = 100,
                    CreatedAt = DateTime.UtcNow.AddHours(8)
                },
                0,
                100);

            var next = await service.ReserveAsync(42, new string('1', 64), new string('2', 64), 2, 100, default);
            Assert.False(next.IsDuplicate);
            await service.CancelAsync(next);
        }
        finally
        {
            await DeleteKeysAsync(connection, $"{instanceName}:security:ai:*");
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RedisProviderGate_EnforcesGlobalLeaseAndOpensCircuit_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("JOKESTER_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var configuration = ConfigurationOptions.Parse(connectionString);
        configuration.AbortOnConnectFail = true;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(configuration);
        var instanceName = $"jokester-ai-provider-test:{Guid.NewGuid():N}";
        var options = new AiCostControlOptions
        {
            DailyImageLimitPerUser = 10,
            DailyPointLimitPerUser = 1000,
            MaxConcurrentTasksPerUser = 2,
            MaxQueuedTasks = 10,
            MaxGlobalProviderConcurrency = 1,
            IdempotencyTtlHours = 1,
            ReservationTtlMinutes = 5,
            ProviderLeaseSeconds = 30,
            ProviderFailureThreshold = 2,
            ProviderFailureWindowSeconds = 30,
            ProviderCircuitOpenSeconds = 30
        };
        var gate = new AiImageProviderGate(
            connection,
            Options.Create(new RedisOptions { InstanceName = instanceName, ConnectionString = connectionString }),
            Options.Create(options),
            NullLogger<AiImageProviderGate>.Instance);

        try
        {
            await using var firstLease = await gate.AcquireAsync(default);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.AcquireAsync(timeout.Token));

            await gate.ReportFailureAsync();
            await gate.ReportFailureAsync();
            var database = connection.GetDatabase();
            Assert.True(await database.KeyExistsAsync($"{instanceName}:security:ai:provider:circuit"));

            var admission = new AiImageAdmissionService(
                connection,
                Options.Create(new RedisOptions { InstanceName = instanceName, ConnectionString = connectionString }),
                Options.Create(options),
                new AiImageTaskQueue(),
                NullLogger<AiImageAdmissionService>.Instance);
            var circuit = await Assert.ThrowsAsync<AppException>(() =>
                admission.ReserveAsync(42, new string('a', 64), new string('b', 64), 1, 10, default));
            Assert.Equal(ErrorCodes.ServiceUnavailable, circuit.Code);
        }
        finally
        {
            await DeleteKeysAsync(connection, $"{instanceName}:security:ai:*");
        }
    }

    private static async Task DeleteKeysAsync(IConnectionMultiplexer connection, RedisValue pattern)
    {
        foreach (var endpoint in connection.GetEndPoints())
        {
            var server = connection.GetServer(endpoint);
            if (!server.IsConnected)
            {
                continue;
            }

            var keys = server.Keys(pattern: pattern).ToArray();
            if (keys.Length > 0)
            {
                await connection.GetDatabase().KeyDeleteAsync(keys);
            }
        }
    }
}
