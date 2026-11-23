using System.Reflection;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Common;
using jokester.admin.Application.DTOs.Users;
using jokester.admin.Application.Services;
using jokester.admin.Authorization;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Controllers;
using jokester.admin.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

/// <summary>
/// /api/users 的服务层鉴权：资料类接口只允许本人或超级管理员，管理类接口只允许超级管理员。
/// </summary>
public sealed partial class UserServiceAuthorizationTests
{
    private const long AliceId = 1;
    private const long BobId = 2;
    private const long AdminId = 3;
    private const long DemotedAdminId = 4;
    private const long DisabledAdminId = 5;

    public static TheoryData<string> SelfServiceOperations => new()
    {
        "point-details", "nickname", "password", "avatar", "signature"
    };

    public static TheoryData<string> AdminOperations => new()
    {
        "page", "menus-tree", "create", "update", "assign-menus", "status", "delete"
    };

    [Theory]
    [MemberData(nameof(SelfServiceOperations))]
    public async Task SelfServiceOperation_OnAnotherUser_ReturnsNotFoundWithoutSideEffects(string operation)
    {
        using var context = CreateContext(currentUserId: AliceId, superAdminClaim: false);
        var before = context.GetUser(BobId);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => InvokeSelfServiceAsync(context.Service, operation, BobId));

        Assert.Equal(ErrorCodes.NotFound, exception.Code);
        // 与用户不存在时的提示一致，不暴露 ID 是否存在。
        var missing = await Assert.ThrowsAsync<NotFoundException>(
            () => InvokeSelfServiceAsync(context.Service, operation, 999));
        Assert.Equal(exception.Message.Replace(BobId.ToString(), "999"), missing.Message);
        context.AssertUserUnchanged(before);
        context.RefreshTokenStore.Verify(
            x => x.RevokeUserSessionsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SelfServiceOperations_OnOwnAccount_Succeed()
    {
        using var context = CreateContext(currentUserId: AliceId, superAdminClaim: false);

        var details = await context.Service.GetPointDetailsAsync(AliceId, new PageQuery(), default);
        await context.Service.UpdateSignatureAsync(AliceId, new UpdateUserSignatureRequest { Signature = "hello" }, default);
        await context.Service.UpdateNickNameAsync(
            AliceId,
            new UpdateUserNickNameRequest { UserName = "alice", NickName = "Alice 2" },
            default);
        await context.Service.UpdatePasswordAsync(AliceId, new UpdateUserPasswordRequest { Password = "new-password" }, default);

        Assert.Equal(1, details.Total);
        Assert.All(details.Items, x => Assert.Equal(AliceId, x.UserId));
        var alice = context.GetUser(AliceId);
        Assert.Equal("hello", alice.Signature);
        Assert.Equal("Alice 2", alice.NickName);
        Assert.Equal("hashed:new-password", alice.PasswordHash);
        Assert.Equal("hash-2", context.GetUser(BobId).PasswordHash);
    }

    [Theory]
    [MemberData(nameof(AdminOperations))]
    public async Task AdminOperation_ByRegularUser_IsForbiddenWithoutSideEffects(string operation)
    {
        using var context = CreateContext(currentUserId: AliceId, superAdminClaim: false);
        var beforeAlice = context.GetUser(AliceId);
        var beforeBob = context.GetUser(BobId);
        var userCount = context.CountUsers();

        // 以自己为目标的 update 就是提权尝试（请求体带 isSuperAdmin=true）。
        var targetId = operation == "update" ? AliceId : BobId;
        var exception = await Assert.ThrowsAsync<AppException>(
            () => InvokeAdminAsync(context.Service, operation, targetId));

        Assert.Equal(ErrorCodes.Forbidden, exception.Code);
        Assert.Equal(MachineErrorCodes.ResourceForbidden, exception.MachineCode);
        context.AssertUserUnchanged(beforeAlice);
        context.AssertUserUnchanged(beforeBob);
        Assert.Equal(userCount, context.CountUsers());
        context.RefreshTokenStore.Verify(
            x => x.RevokeUserSessionsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(DemotedAdminId)]
    [InlineData(DisabledAdminId)]
    public async Task StaleSuperAdminClaim_IsRejected(long currentUserId)
    {
        // Access Token 仍带超级管理员声明，但数据库中已被降权或禁用。
        using var context = CreateContext(currentUserId, superAdminClaim: true);
        var before = context.GetUser(BobId);

        var adminException = await Assert.ThrowsAsync<AppException>(
            () => context.Service.UpdateStatusAsync(BobId, new UpdateUserStatusRequest { Status = 0 }, default));
        await Assert.ThrowsAsync<NotFoundException>(
            () => context.Service.UpdatePasswordAsync(BobId, new UpdateUserPasswordRequest { Password = "x" }, default));
        await Assert.ThrowsAsync<NotFoundException>(
            () => context.Service.GetPointDetailsAsync(BobId, new PageQuery(), default));

        Assert.Equal(ErrorCodes.Forbidden, adminException.Code);
        context.AssertUserUnchanged(before);
    }

    [Fact]
    public async Task SuperAdmin_CanReadAndManageOtherUsers()
    {
        using var context = CreateContext(currentUserId: AdminId, superAdminClaim: true);

        var page = await context.Service.GetPageAsync(new PageQuery(), default);
        var details = await context.Service.GetPointDetailsAsync(BobId, new PageQuery(), default);
        await context.Service.UpdatePasswordAsync(BobId, new UpdateUserPasswordRequest { Password = "reset" }, default);
        await context.Service.UpdateStatusAsync(BobId, new UpdateUserStatusRequest { Status = 0 }, default);

        Assert.Equal(5, page.Total);
        Assert.Equal(1, details.Total);
        Assert.All(details.Items, x => Assert.Equal(BobId, x.UserId));
        var bob = context.GetUser(BobId);
        Assert.Equal("hashed:reset", bob.PasswordHash);
        Assert.Equal(0, bob.Status);
        context.RefreshTokenStore.Verify(
            x => x.RevokeUserSessionsAsync(BobId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task UnauthenticatedCaller_IsUnauthorized()
    {
        using var context = CreateContext(currentUserId: null, superAdminClaim: false);

        var selfService = await Assert.ThrowsAsync<AppException>(
            () => context.Service.GetPointDetailsAsync(BobId, new PageQuery(), default));
        var admin = await Assert.ThrowsAsync<AppException>(
            () => context.Service.GetPageAsync(new PageQuery(), default));

        Assert.Equal(ErrorCodes.Unauthorized, selfService.Code);
        Assert.Equal(ErrorCodes.Unauthorized, admin.Code);
    }

    [Theory]
    [InlineData(nameof(UsersController.GetPointDetails))]
    [InlineData(nameof(UsersController.UpdateNickName))]
    [InlineData(nameof(UsersController.UpdatePassword))]
    [InlineData(nameof(UsersController.UploadAvatar))]
    [InlineData(nameof(UsersController.UpdateSignature))]
    public void SelfServiceActions_DoNotRequireAdminPermissionCodes(string actionName)
    {
        // 普通用户不持有 System.User.* 权限码；这些接口靠服务层的本人校验。
        var action = typeof(UsersController).GetMethod(actionName)!;

        Assert.Empty(action.GetCustomAttributes<PermissionAttribute>());
    }

    [Theory]
    [InlineData(nameof(UsersController.GetPage), "System.User.View")]
    [InlineData(nameof(UsersController.GetPermissionTree), "System.User.Authorize")]
    [InlineData(nameof(UsersController.Create), "System.User.Create")]
    [InlineData(nameof(UsersController.Update), "System.User.Update")]
    [InlineData(nameof(UsersController.AssignMenus), "System.User.Authorize")]
    [InlineData(nameof(UsersController.UpdateStatus), "System.User.UpdateStatus")]
    [InlineData(nameof(UsersController.Delete), "System.User.Delete")]
    public void AdminActions_KeepPermissionCodes(string actionName, string permissionCode)
    {
        var action = typeof(UsersController).GetMethod(actionName)!;

        Assert.Contains(action.GetCustomAttributes<PermissionAttribute>(), x => x.Code == permissionCode);
    }

    private static Task InvokeSelfServiceAsync(UserService service, string operation, long targetId)
    {
        return operation switch
        {
            "point-details" => service.GetPointDetailsAsync(targetId, new PageQuery(), default),
            "nickname" => service.UpdateNickNameAsync(
                targetId,
                new UpdateUserNickNameRequest { UserName = "hijacked", NickName = "hijacked" },
                default),
            "password" => service.UpdatePasswordAsync(targetId, new UpdateUserPasswordRequest { Password = "hijacked" }, default),
            "avatar" => service.UploadAvatarAsync(targetId, Mock.Of<IFormFile>(x => x.Length == 16), default),
            "signature" => service.UpdateSignatureAsync(targetId, new UpdateUserSignatureRequest { Signature = "hijacked" }, default),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
    }

    private static Task InvokeAdminAsync(UserService service, string operation, long targetId)
    {
        return operation switch
        {
            "page" => service.GetPageAsync(new PageQuery(), default),
            "menus-tree" => service.GetPermissionTreeAsync(targetId, null, default),
            "create" => service.CreateAsync(
                new SaveUserRequest { UserName = "mallory", NickName = "mallory", Password = "x", IsSuperAdmin = true },
                default),
            "update" => service.UpdateAsync(
                targetId,
                new UpdateUserInfoRequest { NickName = "escalated", Status = 1, IsSuperAdmin = true },
                default),
            "assign-menus" => service.AssignMenusAsync(targetId, new AssignUserMenusRequest { MenuIds = [1] }, default),
            "status" => service.UpdateStatusAsync(targetId, new UpdateUserStatusRequest { Status = 0 }, default),
            "delete" => service.DeleteAsync(targetId, default),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
    }

    private static UserContext CreateContext(long? currentUserId, bool superAdminClaim)
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
                    updated_at TEXT NULL,
                    UNIQUE (user_id, business_key)
                );

            CREATE TABLE account_deletion_request (user_id INTEGER, status TEXT);
            CREATE TABLE sys_user_membership_entitlement (user_id INTEGER, tier_code TEXT, status TEXT, revoked_at TEXT, starts_at TEXT, expires_at TEXT);
            CREATE TABLE sys_user_status_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                action TEXT NOT NULL,
                reason TEXT NULL,
                operator_user_id INTEGER NOT NULL,
                operator_ip TEXT NULL,
                created_at TEXT NOT NULL
            );
            """);
        var now = DateTime.Now;
        db.Insertable(new[]
        {
            User("alice", "hash-1", status: 1, isSuperAdmin: false, now),
            User("bob", "hash-2", status: 1, isSuperAdmin: false, now),
            User("admin", "hash-3", status: 1, isSuperAdmin: true, now),
            User("demoted", "hash-4", status: 1, isSuperAdmin: false, now),
            User("disabled-admin", "hash-5", status: 0, isSuperAdmin: true, now)
        }).ExecuteCommand();
        db.Insertable(new[]
        {
            Detail(AliceId, 50, now),
            Detail(BobId, 999, now)
        }).ExecuteCommand();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(currentUserId);
        currentUser.SetupGet(x => x.IsSuperAdmin).Returns(superAdminClaim);
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher
            .Setup(x => x.HashPassword(It.IsAny<string>()))
            .Returns((string password) => ($"hashed:{password}", "salt"));
        var refreshTokenStore = new Mock<IRefreshTokenStore>();
        var transition = new UserStatusTransition(db, new HttpContextAccessor());
        var adminService = new UserAdminService(db, currentUser.Object, new PointService(db, currentUser.Object),
            Mock.Of<IPermissionCacheInvalidator>(), refreshTokenStore.Object, transition);
        var service = new UserService(
            db,
            passwordHasher.Object,
            new MapsterMapper.Mapper(),
            Mock.Of<IPermissionCacheInvalidator>(),
            refreshTokenStore.Object,
            Mock.Of<IWebHostEnvironment>(),
            currentUser.Object, adminService, transition);
        return new UserContext(db, service, adminService, refreshTokenStore);
    }

    private static SysUserEntity User(string userName, string passwordHash, int status, bool isSuperAdmin, DateTime now)
    {
        return new SysUserEntity
        {
            UserName = userName,
            NickName = userName,
            PasswordHash = passwordHash,
            Salt = "salt",
            Status = status,
            IsSuperAdmin = isSuperAdmin,
            CreatedAt = now
        };
    }

    private static UserPointDetailEntity Detail(long userId, int points, DateTime now)
    {
        return new UserPointDetailEntity
        {
            UserId = userId,
            ChangePoints = points,
            BalanceAfter = points,
            ChangeType = "gift",
            Source = "register",
            CreatedAt = now
        };
    }

    private sealed class UserContext(
        SqlSugarClient db,
        UserService service,
        UserAdminService adminService,
        Mock<IRefreshTokenStore> refreshTokenStore) : IDisposable
    {
        public UserAdminService Admin { get; } = adminService;
        public SqlSugarClient Db => db;
        public UserService Service { get; } = service;

        public Mock<IRefreshTokenStore> RefreshTokenStore { get; } = refreshTokenStore;

        public SysUserEntity GetUser(long id) => db.Queryable<SysUserEntity>().First(x => x.Id == id);

        public int CountUsers() => db.Queryable<SysUserEntity>().Count();

        public void AssertUserUnchanged(SysUserEntity before)
        {
            var after = GetUser(before.Id);
            Assert.Equal(before.UserName, after.UserName);
            Assert.Equal(before.NickName, after.NickName);
            Assert.Equal(before.PasswordHash, after.PasswordHash);
            Assert.Equal(before.Signature, after.Signature);
            Assert.Equal(before.AvatarUrl, after.AvatarUrl);
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.IsSuperAdmin, after.IsSuperAdmin);
            Assert.Equal(before.IsDeleted, after.IsDeleted);
        }

        public void Dispose() => db.Dispose();
    }
}
