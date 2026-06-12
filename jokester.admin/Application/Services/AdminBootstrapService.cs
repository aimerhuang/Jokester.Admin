using jokester.admin.Application.Abstractions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AdminBootstrapService(ISqlSugarClient db, IPasswordHasher passwordHasher) : IAdminBootstrapService
{
    public async Task<long> UpsertSuperAdminAsync(string userName, string password, CancellationToken cancellationToken)
    {
        var hashed = passwordHasher.HashPassword(password);
        long userId = 0;

        await db.Ado.UseTranAsync(async () =>
        {
            var user = await db.Queryable<SysUserEntity>()
                .FirstAsync(x => x.UserName == userName, cancellationToken);

            if (user is null)
            {
                user = new SysUserEntity
                {
                    UserName = userName,
                    NickName = "系统管理员",
                    PasswordHash = hashed.Hash,
                    Salt = hashed.Salt,
                    Email = $"{userName}@local.admin",
                    Status = 1,
                    IsSuperAdmin = true,
                    Remark = "开发初始化超级管理员",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsDeleted = false
                };
                userId = await db.Insertable(user).ExecuteReturnSnowflakeIdAsync();
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
                role.Id = await db.Insertable(role).ExecuteReturnSnowflakeIdAsync();
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

        return userId;
    }
}
