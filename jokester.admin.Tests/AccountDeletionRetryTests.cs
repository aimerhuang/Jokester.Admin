using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Services;
using jokester.admin.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AccountDeletionRetryTests
{
    [Fact]
    public async Task CreateAsync_RepeatedClientRequestReturnsSingleScheduledRequest()
    {
        SQLitePCL.Batteries_V2.Init();
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        CreateDeletionSchema(db);
        db.Insertable(new SysUserEntity
        {
            Id = 1,
            UserName = "deletion-user",
            PasswordHash = "hash",
            Email = "deletion@example.test",
            Status = 1,
            LastLoginTime = DateTime.Now,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(1);
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(x => x.Verify("current-password", "hash", It.IsAny<string?>())).Returns(true);
        var refreshTokenStore = new Mock<IRefreshTokenStore>();
        var service = CreateService(
            db,
            Mock.Of<IEmailSender>(),
            currentUser: currentUser.Object,
            passwordHasher: passwordHasher.Object,
            refreshTokenStore: refreshTokenStore.Object);
        var request = new Application.DTOs.Auth.CreateAccountDeletionRequest
        {
            CurrentPassword = "current-password",
            Confirmation = "DELETE",
            ClientRequestId = Guid.NewGuid().ToString("D"),
            Reason = "privacy"
        };

        var first = await service.CreateAsync(request, default);
        var repeated = await service.CreateAsync(request, default);

        Assert.Equal(first.RequestId, repeated.RequestId);
        Assert.Equal("scheduled", repeated.Status);
        Assert.Single(db.Queryable<AccountDeletionRequestEntity>().ToList());
        refreshTokenStore.Verify(
            x => x.RevokeUserSessionsAsync(1, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CompletedDeletion_RemovesIdentityFromRetainedPointDetails()
    {
        SQLitePCL.Batteries_V2.Init();
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        CreateDeletionSchema(db);

        var now = DateTime.UtcNow;
        db.Insertable(new SysUserEntity
        {
            Id = 1,
            UserName = "person@example.test",
            NickName = "Person",
            PasswordHash = "unused",
            Email = "person@example.test",
            PointBalance = 50,
            Status = 1,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();
        db.Insertable(new UserPointDetailEntity
        {
            UserId = 1,
            ChangePoints = 50,
            BalanceAfter = 50,
            ChangeType = "gift",
            Source = "register",
            BusinessKey = "register:person@example.test",
            Remark = "person@example.test registered",
            CreatedAt = DateTime.Now
        }).ExecuteCommand();
        db.Insertable(new AccountDeletionRequestEntity
        {
            RequestId = "ADR2026081200000000000000000002",
            UserId = 1,
            ClientRequestHash = new string('C', 64),
            RequestFingerprint = new string('D', 64),
            Status = "scheduled",
            NotificationEmail = "person@example.test",
            RequestedAt = now.AddDays(-8),
            ScheduledDeletionAt = now.AddMinutes(-1),
            CreatedAt = now.AddDays(-8),
            UpdatedAt = now.AddMinutes(-1)
        }).ExecuteCommand();

        var userDirectory = Path.Combine(
            Path.GetTempPath(),
            "jokester-admin-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(userDirectory);
        await File.WriteAllTextAsync(Path.Combine(userDirectory, "private-image.txt"), "private");
        var mediaPathResolver = new Mock<IAiMediaPathResolver>();
        mediaPathResolver.Setup(x => x.ResolveFilePath("1")).Returns(userDirectory);

        try
        {
            var service = CreateService(db, Mock.Of<IEmailSender>(), mediaPathResolver.Object);
            await service.ProcessDueRequestsAsync(default);

            var detail = db.Queryable<UserPointDetailEntity>().Single();
            Assert.DoesNotContain("person@example.test", detail.BusinessKey, StringComparison.OrdinalIgnoreCase);
            Assert.Null(detail.Remark);
            var user = db.Queryable<SysUserEntity>().Single();
            Assert.Null(user.Email);
            Assert.True(user.IsDeleted);
            Assert.False(Directory.Exists(userDirectory));
            Assert.Equal("completed", db.Queryable<AccountDeletionRequestEntity>().Single().Status);
        }
        finally
        {
            if (Directory.Exists(userDirectory)) Directory.Delete(userDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task NotificationFailure_RemainsRetryableAndCompletesOnNextAttempt()
    {
        SQLitePCL.Batteries_V2.Init();
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        CreateSchema(db);
        var now = DateTime.UtcNow;
        db.Insertable(new AccountDeletionRequestEntity
        {
            RequestId = "ADR2026081200000000000000000001",
            UserId = 1,
            ClientRequestHash = new string('A', 64),
            RequestFingerprint = new string('B', 64),
            Status = "notification_pending",
            NotificationEmail = "deleted@example.test",
            RequestedAt = now.AddDays(-8),
            ScheduledDeletionAt = now.AddDays(-1),
            DataDeletedAt = now.AddMinutes(-1),
            CreatedAt = now.AddDays(-8),
            UpdatedAt = now.AddMinutes(-1)
        }).ExecuteCommand();
        var email = new Mock<IEmailSender>();
        email.Setup(x => x.SendAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp unavailable"));
        var service = CreateService(db, email.Object);

        await service.ProcessDueRequestsAsync(default);

        var failed = db.Queryable<AccountDeletionRequestEntity>().Single();
        Assert.Equal("notification_pending", failed.Status);
        Assert.Equal(1, failed.RetryCount);
        Assert.NotNull(failed.NextRetryAt);
        Assert.Null(failed.CompletedAt);

        var retryAt = DateTime.UtcNow.AddSeconds(-1);
        db.Updateable<AccountDeletionRequestEntity>()
            .SetColumns(x => new AccountDeletionRequestEntity { NextRetryAt = retryAt })
            .Where(x => x.Id == failed.Id)
            .ExecuteCommand();
        email.Reset();
        email.Setup(x => x.SendAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await service.ProcessDueRequestsAsync(default);

        var completed = db.Queryable<AccountDeletionRequestEntity>().Single();
        Assert.Equal("completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.NotNull(completed.NotificationSentAt);
        Assert.Null(completed.NotificationEmail);
    }

    private static AccountDeletionService CreateService(
        ISqlSugarClient db,
        IEmailSender emailSender,
        IAiMediaPathResolver? mediaPathResolver = null,
        ICurrentUser? currentUser = null,
        IPasswordHasher? passwordHasher = null,
        IRefreshTokenStore? refreshTokenStore = null)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());
        environment.SetupGet(x => x.WebRootPath).Returns(Path.Combine(Path.GetTempPath(), "unused-web-root"));
        return new AccountDeletionService(
            db,
            currentUser ?? Mock.Of<ICurrentUser>(),
            passwordHasher ?? Mock.Of<IPasswordHasher>(),
            refreshTokenStore ?? Mock.Of<IRefreshTokenStore>(),
            mediaPathResolver ?? Mock.Of<IAiMediaPathResolver>(),
            environment.Object,
            emailSender,
            NullLogger<AccountDeletionService>.Instance);
    }

    private static void CreateSchema(ISqlSugarClient db) => db.Ado.ExecuteCommand("""
        CREATE TABLE account_deletion_request (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            request_id TEXT NOT NULL,
            user_id INTEGER NOT NULL,
            client_request_hash TEXT NOT NULL,
            request_fingerprint TEXT NOT NULL,
            status TEXT NOT NULL,
            reason TEXT NULL,
            notification_email TEXT NULL,
            requested_at TEXT NOT NULL,
            scheduled_deletion_at TEXT NOT NULL,
            cancelled_at TEXT NULL,
            data_deleted_at TEXT NULL,
            completed_at TEXT NULL,
            next_retry_at TEXT NULL,
            retry_count INTEGER NOT NULL DEFAULT 0,
            failure_message TEXT NULL,
            notification_sent_at TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NULL
        );
        """);

    private static void CreateDeletionSchema(ISqlSugarClient db)
    {
        CreateSchema(db);
        db.Ado.ExecuteCommand("""
            CREATE TABLE sys_user (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_name TEXT NOT NULL,
                nick_name TEXT NULL,
                password_hash TEXT NOT NULL,
                salt TEXT NULL,
                email TEXT NULL,
                phone TEXT NULL,
                avatar_url TEXT NULL,
                signature TEXT NULL,
                point_balance INTEGER NOT NULL DEFAULT 0,
                status INTEGER NOT NULL DEFAULT 1,
                is_super_admin INTEGER NOT NULL DEFAULT 0,
                last_login_time TEXT NULL,
                last_login_ip TEXT NULL,
                remark TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE ai_image_favorite (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NULL
            );
            CREATE TABLE ai_image_task (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                prompt TEXT NULL,
                negative_prompt TEXT NULL,
                reference_image_urls TEXT NULL,
                mask_image_url TEXT NULL,
                result_urls TEXT NULL,
                error_message TEXT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NULL
            );
            CREATE TABLE media_asset (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                owner_user_id INTEGER NOT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                deleted_at TEXT NULL
            );
            CREATE TABLE user_consent (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL);
            CREATE TABLE sys_user_role (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL);
            CREATE TABLE sys_user_site (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL);
            CREATE TABLE sys_login_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NULL,
                user_name TEXT NULL,
                ip TEXT NULL,
                user_agent TEXT NULL
            );
            CREATE TABLE sys_operation_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NULL,
                request_data TEXT NULL,
                response_data TEXT NULL,
                ip TEXT NULL
            );
            CREATE TABLE sys_user_point_detail (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                change_points INTEGER NOT NULL,
                balance_after INTEGER NOT NULL,
                change_type TEXT NOT NULL,
                source TEXT NOT NULL,
                business_key TEXT NULL,
                remark TEXT NULL,
                created_at TEXT NOT NULL
            );
            """);
    }
}
