using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Security;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AdminBootstrapService(
    ISqlSugarClient db,
    IPasswordHasher passwordHasher,
    IRefreshTokenStore refreshTokenStore) : IAdminBootstrapService
{
    public async Task<long> UpsertSuperAdminAsync(string userName, string password, CancellationToken cancellationToken)
    {
        var hashed = passwordHasher.HashPassword(password);
        long userId = 0;
        var existingUser = false;
        // 账号为邮箱时按邮箱定位，新建用户沿用注册规则生成用户名，并保存真实邮箱。
        var email = userName.Contains('@') ? userName.Trim().ToLowerInvariant() : null;

        await db.Ado.UseTranAsync(async () =>
        {
            var user = email is null
                ? await db.Queryable<SysUserEntity>().FirstAsync(x => x.UserName == userName, cancellationToken)
                : await db.Queryable<SysUserEntity>().FirstAsync(x => x.Email == email, cancellationToken);

            if (user is null)
            {
                var accountName = userName;
                if (email is not null)
                {
                    accountName = RegistrationIdentityFactory.Create(email).UserName;
                    if (await db.Queryable<SysUserEntity>().AnyAsync(x => x.UserName == accountName, cancellationToken))
                    {
                        accountName = RegistrationIdentityFactory.CreateDisambiguatedUserName(email);
                    }
                }

                user = new SysUserEntity
                {
                    UserName = accountName,
                    NickName = "系统管理员",
                    PasswordHash = hashed.Hash,
                    Salt = hashed.Salt,
                    Email = email ?? $"{userName}@local.admin",
                    Status = 1,
                    IsSuperAdmin = true,
                    Remark = "开发初始化超级管理员",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsDeleted = false
                };
                userId = await db.Insertable(user).ExecuteReturnBigIdentityAsync();
                user.Id = userId;
            }
            else
            {
                user.PasswordHash = hashed.Hash;
                user.Salt = hashed.Salt;
                user.NickName = "系统管理员";
                user.Status = 1;
                user.IsSuperAdmin = true;
                user.IsDeleted = false;
                user.UpdatedAt = DateTime.Now;
                await db.Updateable(user).ExecuteCommandAsync();
                userId = user.Id;
                existingUser = true;
            }

            var role = await db.Queryable<SysRoleEntity>().FirstAsync(x => x.RoleCode == "super_admin", cancellationToken);
            if (role is null)
            {
                role = new SysRoleEntity
                {
                    RoleName = "超级管理员",
                    RoleCode = "super_admin",
                    Status = 1,
                    Remark = "开发初始化超级管理员角色",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsDeleted = false
                };
                role.Id = await db.Insertable(role).ExecuteReturnBigIdentityAsync();
            }
            else
            {
                role.RoleName = "超级管理员";
                role.Status = 1;
                role.IsDeleted = false;
                role.UpdatedAt = DateTime.Now;
                await db.Updateable(role).ExecuteCommandAsync();
            }

            await db.Deleteable<SysUserRoleEntity>().Where(x => x.UserId == userId).ExecuteCommandAsync();
            await db.Insertable(new SysUserRoleEntity
            {
                UserId = userId,
                RoleId = role.Id,
                CreatedAt = DateTime.Now
            }).ExecuteCommandAsync();

            await db.Deleteable<SysUserSiteEntity>().Where(x => x.UserId == userId).ExecuteCommandAsync();
            var sites = await db.Queryable<SysSiteEntity>().Where(x => !x.IsDeleted).ToListAsync(cancellationToken);
            if (sites.Count > 0)
            {
                await db.Insertable(sites.Select(x => new SysUserSiteEntity
                {
                    UserId = userId,
                    SiteId = x.Id,
                    CreatedAt = DateTime.Now
                }).ToList()).ExecuteCommandAsync();
            }

            await db.Deleteable<SysRoleMenuEntity>().Where(x => x.RoleId == role.Id).ExecuteCommandAsync();
            var menus = await db.Queryable<SysMenuEntity>().Where(x => !x.IsDeleted).ToListAsync(cancellationToken);
            if (menus.Count > 0)
            {
                await db.Insertable(menus.Select(x => new SysRoleMenuEntity
                {
                    RoleId = role.Id,
                    MenuId = x.Id,
                    CreatedAt = DateTime.Now
                }).ToList()).ExecuteCommandAsync();
            }
        });

        // 已有账号的密码和超级管理员上下文被重置，撤销其现有 Refresh Token 会话。
        if (existingUser)
        {
            await refreshTokenStore.RevokeUserSessionsAsync(userId, cancellationToken);
        }

        return userId;
    }
}
