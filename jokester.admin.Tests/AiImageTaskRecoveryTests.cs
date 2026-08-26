using System.Reflection;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiImageTaskRecoveryTests
{
    [Fact]
    public async Task RecoverAsync_PartiallyDispatchedBatch_BindsWholeBatchAndRetainsEveryTask()
    {
        using var context = new RecoveryContext();
        var createdAt = DateTime.UtcNow.AddHours(8);
        var tasks = new[]
        {
            CreateTask(createdAt),
            CreateTask(createdAt.AddMilliseconds(1))
        };
        foreach (var task in tasks)
        {
            task.Id = context.Db.Insertable(task).ExecuteReturnBigIdentity();
        }
        var request = CreateRequest(createdAt);
        request.Id = context.Db.Insertable(request).ExecuteReturnBigIdentity();
        context.Db.Insertable(new[]
        {
            new AiImageRequestTaskEntity { RequestId = request.Id, TaskOrdinal = 0, TaskId = tasks[0].Id },
            new AiImageRequestTaskEntity { RequestId = request.Id, TaskOrdinal = 1, TaskId = tasks[1].Id }
        }).ExecuteCommand();
        context.Db.Insertable(new[]
        {
            new AiImageTaskOutboxEntity
            {
                RequestId = request.Id,
                TaskId = tasks[0].Id,
                Status = "dispatched",
                NextAttemptAt = createdAt,
                CreatedAt = createdAt
            },
            new AiImageTaskOutboxEntity
            {
                RequestId = request.Id,
                TaskId = tasks[1].Id,
                Status = "pending",
                NextAttemptAt = createdAt.AddSeconds(-1),
                CreatedAt = createdAt
            }
        }).ExecuteCommand();

        IReadOnlyList<AiImageAdmissionTask>? boundTasks = null;
        context.Admission
            .Setup(x => x.BindBatchAsync(
                It.IsAny<AiImageAdmissionReservation>(),
                request.Id,
                It.IsAny<IReadOnlyList<AiImageAdmissionTask>>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiImageAdmissionReservation, long, IReadOnlyList<AiImageAdmissionTask>, CancellationToken>(
                (_, _, tasks, _) => boundTasks = tasks)
            .Returns(Task.CompletedTask);

        await context.RecoverAsync();

        Assert.NotNull(boundTasks);
        Assert.Equal([(tasks[0].Id, 0), (tasks[1].Id, 1)], boundTasks!.Select(x => (x.TaskId, x.Ordinal)));
        Assert.Equal(2, context.Queue.BacklogCount);
        var outbox = context.Db.Queryable<AiImageTaskOutboxEntity>().OrderBy(x => x.TaskId).ToList();
        Assert.All(outbox, row => Assert.Equal("dispatched", row.Status));
        context.Points.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecoverAsync_UnknownAttemptPastReconcileDeadline_ForcesStableRefundSettlement()
    {
        using var context = new RecoveryContext();
        var now = DateTime.UtcNow.AddHours(8);
        var task = CreateTask(now.AddMinutes(-10));
        task.Status = 3;
        task.StartedAt = now.AddMinutes(-10);
        task.ClaimEpoch = 1;
        task.ClaimTokenHash = null;
        task.LeaseExpiresAt = null;
        task.Id = context.Db.Insertable(task).ExecuteReturnBigIdentity();
        context.Db.Insertable(new AiImageProviderAttemptEntity
        {
            AttemptId = "unknownattempt".PadRight(32, '0'),
            TaskId = task.Id,
            ClaimEpoch = 1,
            ModelReleaseId = 9,
            ReleaseRouteId = 10,
            RouteRole = "primary",
            ConsentProviderCode = "openai",
            UpstreamIdempotencyKey = task.IdempotencyKey,
            State = "provider_unknown",
            StartedAt = now.AddMinutes(-10),
            Deadline = now.AddMinutes(-5),
            ReconcileBy = now.AddMinutes(-1)
        }).ExecuteCommand();

        VersionedImageTaskSettlement? captured = null;
        context.Points
            .Setup(x => x.SettleVersionedImageTaskAsync(
                task.Id,
                2,
                It.IsAny<VersionedImageTaskSettlement>(),
                It.IsAny<string?>(),
                0,
                It.IsAny<CancellationToken>()))
            .Callback<long, int, VersionedImageTaskSettlement, string?, int, CancellationToken>(
                (_, _, settlement, _, _, _) => captured = settlement)
            .ReturnsAsync(new ImageTaskSettlementResult(task.Id, task.UserId, 1, 0, 15, true));
        context.Admission
            .Setup(x => x.CompleteAsync(It.IsAny<AiImageTaskEntity>(), 0, 15))
            .Returns(Task.CompletedTask);

        await context.RecoverAsync();

        Assert.NotNull(captured);
        Assert.Equal(MachineErrorCodes.ProviderOutcomeUnknown, captured!.FailureCode);
        Assert.Equal("provider", captured.FailureStage);
        Assert.True(captured.Retryable);
        Assert.Equal("unknownattempt".PadRight(32, '0'), captured.ProviderAttemptId);
        Assert.Equal("reconciled_failed", captured.ProviderAttemptState);
        Assert.Equal(2, captured.ClaimEpoch);
        context.Points.VerifyAll();
        context.Admission.VerifyAll();
        Assert.Equal(0, context.Queue.BacklogCount);
    }

    [Fact]
    public async Task RecoverAsync_PreparedAttemptWithLiveClaim_IsNotAbandonedOrRequeued()
    {
        using var context = new RecoveryContext();
        var now = DateTime.UtcNow.AddHours(8);
        var task = CreateTask(now.AddMinutes(-10));
        task.Status = 3;
        task.StartedAt = now.AddMinutes(-10);
        task.ClaimEpoch = 1;
        task.ClaimTokenHash = new string('e', 64);
        task.LeaseExpiresAt = now.AddMinutes(1);
        task.Id = context.Db.Insertable(task).ExecuteReturnBigIdentity();
        context.Db.Insertable(new AiImageProviderAttemptEntity
        {
            AttemptId = "preparedattempt".PadRight(32, '0'),
            TaskId = task.Id,
            ClaimEpoch = 1,
            ModelReleaseId = 9,
            ReleaseRouteId = 10,
            RouteRole = "primary",
            ConsentProviderCode = "openai",
            UpstreamIdempotencyKey = task.IdempotencyKey,
            State = "prepared",
            StartedAt = now.AddMinutes(-10),
            Deadline = now.AddMinutes(5),
            ReconcileBy = now.AddMinutes(35)
        }).ExecuteCommand();

        await context.RecoverAsync();

        Assert.Equal("prepared", context.Db.Queryable<AiImageProviderAttemptEntity>().Single().State);
        var unchanged = context.Db.Queryable<AiImageTaskEntity>().Single();
        Assert.Equal(3, unchanged.Status);
        Assert.Equal(new string('e', 64), unchanged.ClaimTokenHash);
        Assert.Equal(0, context.Queue.BacklogCount);
        context.Points.VerifyNoOtherCalls();
        context.Admission.VerifyNoOtherCalls();
    }

    private static AiImageRequestEntity CreateRequest(DateTime createdAt) => new()
    {
        UserId = 7,
        IdempotencyKeyHash = new string('a', 64),
        CanonicalPayloadHash = new string('b', 64),
        CanonicalizationVersion = AiImageCatalogService.SizeContractVersion,
        NormalizationProfile = "native-v1",
        SizeContractVersion = AiImageCatalogService.SizeContractVersion,
        ModelReleaseId = 9,
        AdmissionReservationId = "owner-token",
        AdmissionQuotaDate = createdAt.ToString("yyyyMMdd"),
        ReservedPointCost = 30,
        RequestedImageCount = 2,
        TaskCount = 2,
        LegacyBatchShape = "split-task-per-image",
        Status = "active",
        CreatedAt = createdAt
    };

    private static AiImageTaskEntity CreateTask(DateTime createdAt) => new()
    {
        SiteId = 1,
        UserId = 7,
        Prompt = "recovery test",
        ModelName = "gpt-image-2",
        ModelCode = "gpt-image-2",
        SizeContractVersion = AiImageCatalogService.SizeContractVersion,
        SizeMode = "auto",
        RequestedSize = "auto",
        ModelReleaseId = 9,
        PriceId = 11,
        PriceReleaseId = 9,
        UnitPointCost = 15,
        ImageCount = 1,
        IdempotencyKey = Guid.NewGuid().ToString("N").PadRight(64, '0'),
        RequestFingerprint = new string('f', 64),
        PointCost = 15,
        BillingStatus = 0,
        RefundedPoints = 0,
        ResolutionCode = null,
        QualityCode = "med",
        AspectRatioCode = null,
        Width = 0,
        Height = 0,
        Size = "auto",
        Quality = "medium",
        Status = 0,
        CreatedAt = createdAt
    };

    private sealed class RecoveryContext : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly AiImageTaskRecoveryWorker worker;

        public RecoveryContext()
        {
            SQLitePCL.Batteries_V2.Init();
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=:memory:",
                DbType = SqlSugar.DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute
            });
            Db.Ado.ExecuteCommand("""
                CREATE TABLE ai_image_task (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    site_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    source_prompt_id INTEGER NULL,
                    prompt TEXT NOT NULL,
                    negative_prompt TEXT NULL,
                    prompt_policy_version INTEGER NOT NULL DEFAULT 0,
                    prompt_checked_at TEXT NULL,
                    model_name TEXT NULL,
                    model_code TEXT NULL,
                    size_contract_version TEXT NULL,
                    size_mode TEXT NULL,
                    requested_size TEXT NULL,
                    requested_width INTEGER NULL,
                    requested_height INTEGER NULL,
                    output_width INTEGER NULL,
                    output_height INTEGER NULL,
                    output_size TEXT NULL,
                    output_mime_type TEXT NULL,
                    model_release_id INTEGER NULL,
                    price_id INTEGER NULL,
                    price_release_id INTEGER NULL,
                    unit_point_cost INTEGER NULL,
                    image_count INTEGER NOT NULL,
                    completed_image_count INTEGER NOT NULL DEFAULT 0,
                    idempotency_key TEXT NOT NULL,
                    request_fingerprint TEXT NOT NULL,
                    point_cost INTEGER NOT NULL,
                    billing_status INTEGER NOT NULL DEFAULT 0,
                    refunded_points INTEGER NULL,
                    resolution_code TEXT NULL,
                    quality_code TEXT NOT NULL,
                    aspect_ratio_code TEXT NULL,
                    width INTEGER NOT NULL,
                    height INTEGER NOT NULL,
                    size TEXT NOT NULL,
                    quality TEXT NOT NULL,
                    reference_image_urls TEXT NULL,
                    mask_image_url TEXT NULL,
                    result_urls TEXT NULL,
                    status INTEGER NOT NULL DEFAULT 0,
                    error_message TEXT NULL,
                    failure_code TEXT NULL,
                    failure_stage TEXT NULL,
                    retryable INTEGER NULL,
                    claim_epoch INTEGER NOT NULL DEFAULT 0,
                    claim_token_hash TEXT NULL,
                    lease_expires_at TEXT NULL,
                    heartbeat_at TEXT NULL,
                    started_at TEXT NULL,
                    completed_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE ai_image_request_idempotency (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    idempotency_key_hash TEXT NOT NULL,
                    canonical_payload_hash TEXT NOT NULL,
                    canonicalization_version TEXT NOT NULL,
                    normalization_profile TEXT NOT NULL,
                    size_contract_version TEXT NOT NULL,
                    model_release_id INTEGER NULL,
                    admission_reservation_id TEXT NULL,
                    admission_quota_date TEXT NULL,
                    reserved_point_cost INTEGER NOT NULL,
                    requested_image_count INTEGER NOT NULL,
                    task_count INTEGER NOT NULL,
                    legacy_batch_shape TEXT NOT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE ai_image_request_task (
                    request_id INTEGER NOT NULL,
                    task_ordinal INTEGER NOT NULL,
                    task_id INTEGER NOT NULL,
                    PRIMARY KEY (request_id, task_ordinal)
                );
                CREATE TABLE ai_image_task_outbox (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    request_id INTEGER NOT NULL,
                    task_id INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    attempt_count INTEGER NOT NULL DEFAULT 0,
                    next_attempt_at TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );
                CREATE TABLE ai_image_provider_attempt (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    attempt_id TEXT NOT NULL,
                    task_id INTEGER NOT NULL,
                    claim_epoch INTEGER NOT NULL,
                    model_release_id INTEGER NULL,
                    release_route_id INTEGER NULL,
                    route_role TEXT NULL,
                    consent_provider_code TEXT NULL,
                    upstream_idempotency_key TEXT NOT NULL,
                    state TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    deadline TEXT NOT NULL,
                    reconcile_by TEXT NOT NULL,
                    completed_at TEXT NULL
                );
                """);

            Queue = new AiImageTaskQueue();
            Points = new Mock<IPointService>(MockBehavior.Strict);
            Admission = new Mock<IAiImageAdmissionService>(MockBehavior.Strict);
            provider = new ServiceCollection()
                .AddSingleton<ISqlSugarClient>(Db)
                .AddSingleton(Points.Object)
                .AddSingleton(Admission.Object)
                .BuildServiceProvider();
            worker = new AiImageTaskRecoveryWorker(
                Queue,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new AiCostControlOptions
                {
                    MaxQueuedTasks = 20,
                    OutboxBindDeadlineMinutes = 120
                }),
                NullLogger<AiImageTaskRecoveryWorker>.Instance);
        }

        public SqlSugarClient Db { get; }

        public AiImageTaskQueue Queue { get; }

        public Mock<IPointService> Points { get; }

        public Mock<IAiImageAdmissionService> Admission { get; }

        public async Task RecoverAsync()
        {
            var method = typeof(AiImageTaskRecoveryWorker).GetMethod(
                "RecoverAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(AiImageTaskRecoveryWorker), "RecoverAsync");
            var task = (Task?)method.Invoke(worker, [CancellationToken.None])
                ?? throw new InvalidOperationException("Recovery invocation did not return a task.");
            await task;
        }

        public void Dispose()
        {
            provider.Dispose();
            Db.Dispose();
        }
    }
}
