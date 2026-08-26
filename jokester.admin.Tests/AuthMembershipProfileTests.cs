using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Tests;

public sealed class AuthMembershipProfileTests
{
    [Fact]
    public async Task GetProfileAsync_ReturnsLatestActiveMonthlyVipWhenPointsAreExhausted()
    {
        using var context = new TestContext(pointBalance: 0);
        var now = DateTime.Now;
        context.InsertEntitlement("recharge:redeem:1", now.AddDays(-2), now.AddDays(5));
        context.InsertEntitlement("recharge:redeem:2", now.AddDays(-1), now.AddDays(20));

        var profile = await context.Service.GetProfileAsync(default);

        Assert.Equal(0, profile.PointBalance);
        Assert.NotNull(profile.Membership);
        Assert.Equal("monthly_vip", profile.Membership.TierCode);
        Assert.Equal("active", profile.Membership.Status);
        Assert.Equal(DateTimeKind.Utc, profile.Membership.ExpiresAt.Kind);
        Assert.Equal(
            ApiDateTime.FromLocalStorage(now.AddDays(20)),
            profile.Membership.ExpiresAt,
            TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("active", -10, -1, false)]
    [InlineData("revoked", -1, 10, true)]
    [InlineData("active", 1, 10, false)]
    public async Task GetProfileAsync_IgnoresInactiveEntitlements(
        string status,
        int startsInDays,
        int expiresInDays,
        bool revoked)
    {
        using var context = new TestContext(pointBalance: 0);
        var now = DateTime.Now;
        context.InsertEntitlement(
            "recharge:redeem:inactive",
            now.AddDays(startsInDays),
            now.AddDays(expiresInDays),
            status,
            revoked ? now : null);

        var profile = await context.Service.GetProfileAsync(default);

        Assert.Null(profile.Membership);
    }

    [Fact]
    public async Task GetProfileAsync_DoesNotInferVipFromSignInPointBucket()
    {
        using var context = new TestContext(pointBalance: 25);
        context.Db.Insertable(new PointBucketEntity
        {
            UserId = 1,
            Source = "sign_in",
            BusinessKey = "sign-in:1:20260820",
            GrantedPoints = 25,
            RemainingPoints = 25,
            ExpiresAt = DateTime.Now.AddHours(1),
            SpendPriority = 100,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();

        var profile = await context.Service.GetProfileAsync(default);

        Assert.Equal(25, profile.PointBalance);
        Assert.Null(profile.Membership);
    }

    [Fact]
    public async Task LoginAndBothRefreshPaths_IncludeActiveMembership()
    {
        using var context = new TestContext(pointBalance: 0);
        context.InsertEntitlement(
            "recharge:redeem:login",
            DateTime.Now.AddDays(-1),
            DateTime.Now.AddDays(29));

        var login = await context.Service.LoginAsync(new LoginRequest
        {
            UserName = "member",
            Password = "password"
        }, default);

        context.RefreshTokenStore
            .Setup(x => x.ConsumeAsync("normal-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenConsumeResult(
                RefreshTokenConsumeStatus.Succeeded,
                1,
                "session-normal"));
        var refreshed = await context.Service.RefreshAsync(
            new RefreshTokenRequest { RefreshToken = "normal-refresh" },
            default);

        context.RefreshTokenStore
            .Setup(x => x.ConsumeAsync("concurrent-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenConsumeResult(
                RefreshTokenConsumeStatus.Concurrent,
                1,
                "session-concurrent",
                new RefreshTokenRotationTokens(
                    "existing-access",
                    "existing-refresh",
                    DateTime.UtcNow.AddMinutes(10),
                    DateTime.UtcNow.AddDays(7))));
        var concurrent = await context.Service.RefreshAsync(
            new RefreshTokenRequest { RefreshToken = "concurrent-refresh" },
            default);

        Assert.Equal("monthly_vip", login.User.Membership?.TierCode);
        Assert.Equal("monthly_vip", refreshed.User.Membership?.TierCode);
        Assert.Equal("monthly_vip", concurrent.User.Membership?.TierCode);
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(int pointBalance)
        {
            SQLitePCL.Batteries_V2.Init();
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=:memory:",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute
            });
            CreateSchema();
            Db.Insertable(new SysUserEntity
            {
                Id = 1,
                UserName = "member",
                NickName = "Member",
                PasswordHash = "hash",
                PointBalance = pointBalance,
                Status = 1,
                CreatedAt = DateTime.Now
            }).ExecuteCommand();

            var redis = new Mock<IDatabase>();
            redis.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);
            redis.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            redis.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);
            var connection = new Mock<IConnectionMultiplexer>();
            connection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(redis.Object);

            var passwordHasher = new Mock<IPasswordHasher>();
            passwordHasher.Setup(x => x.Verify("password", "hash", It.IsAny<string?>()))
                .Returns(true);
            var tokenService = new Mock<ITokenService>();
            tokenService.Setup(x => x.CreateAccessToken(1, "member", false, It.IsAny<string>()))
                .Returns("access-token");
            tokenService.Setup(x => x.CreateRefreshToken()).Returns("replacement-refresh");
            tokenService.Setup(x => x.GetAccessTokenExpiresAt())
                .Returns(DateTime.UtcNow.AddMinutes(15));
            RefreshTokenStore = new Mock<IRefreshTokenStore>();
            RefreshTokenStore
                .Setup(x => x.SaveAsync(
                    It.IsAny<string>(),
                    1,
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            RefreshTokenStore
                .Setup(x => x.CompleteRotationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<RefreshTokenRotationTokens>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var permissionService = new Mock<IPermissionService>();
            permissionService
                .Setup(x => x.GetPermissionsAsync(1, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<string>());
            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(1);
            var auditLog = new Mock<IAuditLogWriter>();
            auditLog
                .Setup(x => x.WriteLoginAsync(
                    It.IsAny<long?>(),
                    It.IsAny<string?>(),
                    It.IsAny<bool>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Service = new AuthService(
                Db,
                passwordHasher.Object,
                tokenService.Object,
                RefreshTokenStore.Object,
                permissionService.Object,
                currentUser.Object,
                auditLog.Object,
                Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IBlogCaptchaService>(),
                connection.Object,
                Mock.Of<IAppleAppAccountTokenService>(),
                Options.Create(new AppleAppStoreOptions { Enabled = false }),
                Options.Create(new JwtOptions
                {
                    Issuer = "membership-tests",
                    RefreshTokenExpiresDays = 7
                }));
        }

        public SqlSugarClient Db { get; }

        public Mock<IRefreshTokenStore> RefreshTokenStore { get; }

        public AuthService Service { get; }

        public void InsertEntitlement(
            string businessKey,
            DateTime startsAt,
            DateTime expiresAt,
            string status = "active",
            DateTime? revokedAt = null)
        {
            Db.Insertable(new SysUserMembershipEntitlementEntity
            {
                UserId = 1,
                TierCode = "monthly_vip",
                Source = "recharge",
                BusinessKey = businessKey,
                StartsAt = startsAt,
                ExpiresAt = expiresAt,
                Status = status,
                RevokedAt = revokedAt,
                CreatedAt = startsAt
            }).ExecuteCommand();
        }

        private void CreateSchema() => Db.Ado.ExecuteCommand("""
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
            CREATE TABLE sys_user_point_bucket (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                source TEXT NOT NULL,
                business_key TEXT NOT NULL,
                granted_points INTEGER NOT NULL,
                remaining_points INTEGER NOT NULL,
                expired_points INTEGER NOT NULL DEFAULT 0,
                expires_at TEXT NULL,
                spend_priority INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL
            );
            CREATE TABLE sys_user_membership_entitlement (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                tier_code TEXT NOT NULL,
                source TEXT NOT NULL,
                business_key TEXT NOT NULL UNIQUE,
                starts_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                status TEXT NOT NULL,
                revoked_at TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL
            );
            CREATE TABLE sys_site (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                site_name TEXT NOT NULL,
                site_code TEXT NOT NULL,
                status INTEGER NOT NULL,
                sort INTEGER NOT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE sys_user_site (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                site_id INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            """);

        public void Dispose() => Db.Dispose();
    }
}
