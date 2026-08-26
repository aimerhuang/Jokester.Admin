using jokester.admin.Application.DTOs.Common;
using jokester.admin.Application.Services;
using jokester.admin.Application.Abstractions;
using jokester.admin.Domain.Entities;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class PointDetailsTests
{
    [Fact]
    public async Task GetDetailsAsync_ReturnsOnlyCurrentUsersNewestDetails()
    {
        SQLitePCL.Batteries_V2.Init();
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute
        });
        db.Ado.ExecuteCommand("""
            CREATE TABLE sys_user (
                id INTEGER PRIMARY KEY,
                user_name TEXT NOT NULL,
                nick_name TEXT NULL,
                password_hash TEXT NOT NULL,
                salt TEXT NULL,
                email TEXT NULL,
                phone TEXT NULL,
                avatar_url TEXT NULL,
                signature TEXT NULL,
                point_balance INTEGER NOT NULL DEFAULT 0,
                status INTEGER NOT NULL,
                is_super_admin INTEGER NOT NULL DEFAULT 0,
                last_login_time TEXT NULL,
                last_login_ip TEXT NULL,
                remark TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE sys_user_point_detail (
                id INTEGER PRIMARY KEY,
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
        db.Insertable(new[]
        {
            new SysUserEntity { UserName = "current", PasswordHash = "x", Status = 1, CreatedAt = DateTime.Now },
            new SysUserEntity { UserName = "other", PasswordHash = "x", Status = 1, CreatedAt = DateTime.Now }
        }).ExecuteCommand();
        var now = DateTime.Now;
        db.Insertable(new[]
        {
            Detail(1, 1, 50, now.AddMinutes(-2)),
            Detail(2, 2, 999, now),
            Detail(3, 1, -10, now.AddMinutes(-1))
        }).ExecuteCommand();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(1);
        var service = new PointService(db, currentUser.Object);

        var result = await service.GetDetailsAsync(new PageQuery { PageIndex = 1, PageSize = 10 }, default);

        Assert.Equal(2, result.Total);
        Assert.Equal([3L, 1L], result.Items.Select(x => x.Id));
        Assert.DoesNotContain(result.Items, x => x.ChangePoints == 999);
        Assert.False(result.HasMore);
        Assert.All(result.Items, item => Assert.Equal(DateTimeKind.Utc, item.CreatedAt.Kind));
    }

    [Fact]
    public async Task SignInAsync_ReturnsUtcExpiry()
    {
        using var context = CreateContext();
        var service = new PointService(context.Db, context.CurrentUser.Object);

        var result = await service.SignInAsync(default);

        Assert.Equal(DateTimeKind.Utc, result.ExpireAt.Kind);
        Assert.EndsWith("Z\"", System.Text.Json.JsonSerializer.Serialize(result.ExpireAt), StringComparison.Ordinal);
    }

    [Fact]
    public void PageQuery_ClampsPageIndexToOne()
    {
        Assert.Equal(1, new PageQuery { PageIndex = 0 }.PageIndex);
    }

    private static PointContext CreateContext()
    {
        SQLitePCL.Batteries_V2.Init();
        var config = new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        };
        var db = new SqlSugarClient(config);
        db.Ado.ExecuteCommand("""
            CREATE TABLE sys_user (
                id INTEGER PRIMARY KEY,
                user_name TEXT NOT NULL,
                nick_name TEXT NULL,
                password_hash TEXT NOT NULL,
                salt TEXT NULL,
                email TEXT NULL,
                phone TEXT NULL,
                avatar_url TEXT NULL,
                signature TEXT NULL,
                point_balance INTEGER NOT NULL DEFAULT 0,
                status INTEGER NOT NULL,
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
            """);
        db.Insertable(new SysUserEntity
        {
            Id = 1,
            UserName = "current",
            PasswordHash = "x",
            Status = 1,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(1);
        return new PointContext(db, currentUser);
    }

    private sealed record PointContext(SqlSugarClient Db, Mock<ICurrentUser> CurrentUser) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static UserPointDetailEntity Detail(long id, long userId, int points, DateTime createdAt) => new()
    {
        Id = id,
        UserId = userId,
        ChangePoints = points,
        BalanceAfter = points,
        ChangeType = points >= 0 ? "gift" : "consume",
        Source = "test",
        CreatedAt = createdAt
    };
}
