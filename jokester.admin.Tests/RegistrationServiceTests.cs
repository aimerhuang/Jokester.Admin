using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Application.Services;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Tests;

public sealed class RegistrationServiceTests
{
    [Fact]
    public async Task RegisterAsync_CreatesTheAccountAndReturnsSuccessWhenCodeCleanupFails()
    {
        const string normalizedEmail = "new.user@example.test";
        const string emailCodeKey = "test:register_email_code:new.user@example.test";
        using var db = CreateDatabase();

        var redis = new Mock<IDatabase>();
        redis
            .Setup(x => x.StringGetAsync((RedisKey)emailCodeKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"123456");
        redis
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable after commit"));

        var connection = new Mock<IConnectionMultiplexer>();
        connection
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(redis.Object);

        var emailValidation = new Mock<IEmailValidationService>();
        emailValidation
            .Setup(x => x.ValidateAndNormalizeAsync(" New.User@Example.Test ", It.IsAny<CancellationToken>()))
            .ReturnsAsync(normalizedEmail);

        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher
            .Setup(x => x.HashPassword("password123"))
            .Returns(("password-hash", "password-salt"));

        var service = new RegistrationService(
            db,
            passwordHasher.Object,
            emailValidation.Object,
            Mock.Of<IEmailSender>(),
            connection.Object,
            Options.Create(new RedisOptions { InstanceName = "test:" }),
            NullLogger<RegistrationService>.Instance);

        var result = await service.RegisterAsync(
            new RegisterRequest
            {
                Email = " New.User@Example.Test ",
                EmailCode = "123456",
                Password = "password123"
            },
            CancellationToken.None);

        var user = db.Queryable<SysUserEntity>().First(x => x.Id == result.UserId);
        Assert.NotNull(user);
        Assert.Equal("new.user", user.UserName);
        Assert.Equal("new.user", user.NickName);
        Assert.Equal(normalizedEmail, user.Email);
        Assert.Equal("password-hash", user.PasswordHash);
        Assert.Equal("password-salt", user.Salt);
        Assert.Equal(50, user.PointBalance);

        var role = db.Queryable<SysRoleEntity>().First(x => x.RoleCode == "ai_operator");
        var site = db.Queryable<SysSiteEntity>().First(x => x.SiteCode == "ai_image");
        var userRole = db.Queryable<SysUserRoleEntity>().First(x => x.UserId == result.UserId);
        var userSite = db.Queryable<SysUserSiteEntity>().First(x => x.UserId == result.UserId);
        Assert.Equal(role.Id, userRole.RoleId);
        Assert.Equal(site.Id, userSite.SiteId);

        var gift = db.Queryable<UserPointDetailEntity>().First(x => x.UserId == result.UserId);
        Assert.Equal(50, gift.ChangePoints);
        Assert.Equal(50, gift.BalanceAfter);
        Assert.Equal("gift", gift.ChangeType);
        Assert.Equal("register", gift.Source);
        Assert.Equal($"register:user:{result.UserId}", gift.BusinessKey);

        redis.Verify(x => x.ScriptEvaluateAsync(
            It.Is<string>(script => script.Contains("redis.call('get'", StringComparison.Ordinal)),
            It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == (RedisKey)emailCodeKey),
            It.Is<RedisValue[]>(values => values.Length == 1 && values[0] == (RedisValue)"123456"),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    private static SqlSugarClient CreateDatabase()
    {
        SQLitePCL.Batteries_V2.Init();
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.Ado.ExecuteCommand("""
            CREATE TABLE sys_user (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_name TEXT NOT NULL UNIQUE,
                nick_name TEXT NULL,
                password_hash TEXT NOT NULL,
                salt TEXT NULL,
                email TEXT NULL UNIQUE,
                phone TEXT NULL,
                avatar_url TEXT NULL,
                signature TEXT NULL,
                point_balance INTEGER NOT NULL,
                status INTEGER NOT NULL,
                is_super_admin INTEGER NOT NULL,
                last_login_time TEXT NULL,
                last_login_ip TEXT NULL,
                remark TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                is_deleted INTEGER NOT NULL
            );
            CREATE TABLE sys_role (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                role_name TEXT NOT NULL,
                role_code TEXT NOT NULL UNIQUE,
                status INTEGER NOT NULL,
                remark TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                is_deleted INTEGER NOT NULL
            );
            CREATE TABLE sys_site (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                site_name TEXT NOT NULL,
                site_code TEXT NOT NULL UNIQUE,
                domain TEXT NULL,
                description TEXT NULL,
                status INTEGER NOT NULL,
                sort INTEGER NOT NULL,
                is_deleted INTEGER NOT NULL
            );
            CREATE TABLE sys_user_role (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                role_id INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE sys_user_site (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                site_id INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE sys_user_point_detail (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                change_points INTEGER NOT NULL,
                balance_after INTEGER NOT NULL,
                change_type TEXT NOT NULL,
                source TEXT NOT NULL,
                business_key TEXT NULL UNIQUE,
                remark TEXT NULL,
                created_at TEXT NOT NULL
            );
            INSERT INTO sys_role
                (role_name, role_code, status, remark, created_at, updated_at, is_deleted)
            VALUES
                ('AI operator', 'ai_operator', 1, NULL, CURRENT_TIMESTAMP, NULL, 0);
            INSERT INTO sys_site
                (site_name, site_code, domain, description, status, sort, is_deleted)
            VALUES
                ('AI image', 'ai_image', NULL, NULL, 1, 0, 0);
            """);
        return db;
    }
}
