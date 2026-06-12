using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Common;
using jokester.admin.Application.DTOs.Users;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using MapsterMapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class UserService(
    ISqlSugarClient db,
    IPasswordHasher passwordHasher,
    IMapper mapper,
    IPermissionCacheInvalidator permissionCacheInvalidator,
    IWebHostEnvironment environment) : IUserService
{
    private static readonly HashSet<string> AllowedAvatarMimeTypes =
    [
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/svg+xml"
    ];

    private const long MaxAvatarFileSizeBytes = 10 * 1024 * 1024;
    public async Task<PagedResult<UserListItemDto>> GetPageAsync(PageQuery query, CancellationToken cancellationToken)
    {
        var total = await db.Queryable<SysUserEntity>().Where(x => !x.IsDeleted).CountAsync();
        var users = await db.Queryable<SysUserEntity>()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.Id)
            .ToPageListAsync(query.PageIndex, query.PageSize);

        if (users.Count == 0)
        {
            return new PagedResult<UserListItemDto>
            {
                Total = total,
                PageIndex = query.PageIndex,
                PageSize = query.PageSize
            };
        }

        var userIds = users.Select(x => x.Id).ToList();
        var userRoles = await db.Queryable<SysUserRoleEntity>().Where(x => userIds.Contains(x.UserId)).ToListAsync();
        var userSites = await db.Queryable<SysUserSiteEntity>().Where(x => userIds.Contains(x.UserId)).ToListAsync();

        var roleLookup = userRoles.GroupBy(x => x.UserId).ToDictionary(x => x.Key, x => (IReadOnlyCollection<long>)x.Select(y => y.RoleId).ToArray());
        var siteLookup = userSites.GroupBy(x => x.UserId).ToDictionary(x => x.Key, x => (IReadOnlyCollection<long>)x.Select(y => y.SiteId).ToArray());

        var items = users.Select(x =>
        {
            var dto = mapper.Map<UserListItemDto>(x);
            return new UserListItemDto
            {
                Id = dto.Id,
                UserName = dto.UserName,
                NickName = dto.NickName,
                Email = dto.Email,
                Phone = dto.Phone,
                AvatarUrl = dto.AvatarUrl,
                Signature = dto.Signature,
                PointBalance = dto.PointBalance,
                Status = dto.Status,
                IsSuperAdmin = dto.IsSuperAdmin,
                RoleIds = roleLookup.GetValueOrDefault(x.Id, Array.Empty<long>()),
                SiteIds = siteLookup.GetValueOrDefault(x.Id, Array.Empty<long>())
            };
        }).ToArray();

        return new PagedResult<UserListItemDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<PagedResult<UserPointDetailDto>> GetPointDetailsAsync(long id, PageQuery query, CancellationToken cancellationToken)
    {
        var exists = await db.Queryable<SysUserEntity>().AnyAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"用户不存在: {id}");
        }

        RefAsync<int> total = 0;
        var items = await db.Queryable<UserPointDetailEntity>()
            .Where(x => x.UserId == id)
            .OrderBy(x => x.CreatedAt, OrderByType.Desc)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .Select(x => new UserPointDetailDto
            {
                Id = x.Id,
                UserId = x.UserId,
                ChangePoints = x.ChangePoints,
                BalanceAfter = x.BalanceAfter,
                ChangeType = x.ChangeType,
                Source = x.Source,
                Remark = x.Remark,
                CreatedAt = x.CreatedAt
            })
            .ToPageListAsync(query.PageIndex, query.PageSize, total);

        return new PagedResult<UserPointDetailDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<UserPermissionTreeDto> GetPermissionTreeAsync(long id, long? siteId, CancellationToken cancellationToken)
    {
        await EnsureUserExistsAsync(id, cancellationToken);

        var roleIds = await db.Queryable<SysUserRoleEntity>()
            .Where(x => x.UserId == id)
            .Select(x => x.RoleId)
            .ToListAsync(cancellationToken);

        var grantedMenuIds = roleIds.Count == 0
            ? []
            : await db.Queryable<SysRoleMenuEntity>()
                .Where(x => roleIds.Contains(x.RoleId))
                .Select(x => x.MenuId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var menus = await db.Queryable<SysMenuEntity>()
            .Where(x => !x.IsDeleted)
            .WhereIF(siteId.HasValue, x => x.SiteId == siteId!.Value)
            .OrderBy(x => x.SiteId)
            .OrderBy(x => x.ParentId)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var grantedSet = grantedMenuIds.ToHashSet();
        return new UserPermissionTreeDto
        {
            UserId = id,
            RoleIds = roleIds,
            GrantedMenuIds = grantedMenuIds,
            Tree = BuildUserPermissionTree(menus, grantedSet)
        };
    }

    public async Task<long> CreateAsync(SaveUserRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserNameAvailableAsync(request.UserName, null);

        var hashed = passwordHasher.HashPassword(request.Password);
        var entity = new SysUserEntity
        {
            UserName = request.UserName,
            NickName = request.NickName,
            PasswordHash = hashed.Hash,
            Salt = hashed.Salt,
            Email = request.Email,
            Phone = request.Phone,
            AvatarUrl = request.AvatarUrl,
            PointBalance = 0,
            Status = request.Status,
            IsSuperAdmin = request.IsSuperAdmin,
            Remark = request.Remark,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDeleted = false
        };

        await db.Ado.BeginTranAsync();
        try
        {
            var userId = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
            await SyncUserRolesAsync(userId, request.RoleIds);
            await SyncUserSitesAsync(userId, request.SiteIds);
            await db.Ado.CommitTranAsync();
            await permissionCacheInvalidator.RemoveUserAsync(userId, cancellationToken);
            return userId;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task UpdateAsync(long id, UpdateUserInfoRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.Queryable<SysUserEntity>().FirstAsync(x => x.Id == id && !x.IsDeleted);
        if (entity is null)
        {
            throw new NotFoundException($"用户不存在: {id}");
        }

        entity.NickName = request.NickName;
        entity.Email = request.Email;
        entity.Phone = request.Phone;
        entity.Status = request.Status;
        entity.IsSuperAdmin = request.IsSuperAdmin;
        entity.Remark = request.Remark;
        entity.UpdatedAt = DateTime.Now;

        await db.Ado.BeginTranAsync();
        try
        {
            await db.Updateable(entity).ExecuteCommandAsync();
            await SyncUserRolesAsync(id, request.RoleIds);
            await SyncUserSitesAsync(id, request.SiteIds);
            await db.Ado.CommitTranAsync();
            await permissionCacheInvalidator.RemoveUserAsync(id, cancellationToken);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task AssignMenusAsync(long id, AssignUserMenusRequest request, CancellationToken cancellationToken)
    {
        var user = await EnsureUserExistsAsync(id, cancellationToken);
        var menuIds = request.MenuIds.Distinct().ToArray();

        if (menuIds.Length > 0)
        {
            var validMenuIds = await db.Queryable<SysMenuEntity>()
                .Where(x => menuIds.Contains(x.Id) && !x.IsDeleted)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            var invalidMenuIds = menuIds.Except(validMenuIds).ToArray();
            if (invalidMenuIds.Length > 0)
            {
                throw new NotFoundException($"菜单不存在: {string.Join(",", invalidMenuIds)}");
            }
        }

        await db.Ado.BeginTranAsync();
        try
        {
            var roleId = await GetOrCreateUserGrantRoleAsync(user);
            var bound = await db.Queryable<SysUserRoleEntity>()
                .AnyAsync(x => x.UserId == id && x.RoleId == roleId, cancellationToken);
            if (!bound)
            {
                await db.Insertable(new SysUserRoleEntity
                {
                    UserId = id,
                    RoleId = roleId,
                    CreatedAt = DateTime.Now
                }).ExecuteCommandAsync();
            }

            await db.Deleteable<SysRoleMenuEntity>().Where(x => x.RoleId == roleId).ExecuteCommandAsync();
            if (menuIds.Length > 0)
            {
                await db.Insertable(menuIds.Select(menuId => new SysRoleMenuEntity
                {
                    RoleId = roleId,
                    MenuId = menuId,
                    CreatedAt = DateTime.Now
                }).ToList()).ExecuteCommandAsync();
            }

            await db.Ado.CommitTranAsync();
            await permissionCacheInvalidator.RemoveUserAsync(id, cancellationToken);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task UpdateNickNameAsync(long id, UpdateUserNickNameRequest request, CancellationToken cancellationToken)
    {
        var userName = request.UserName.Trim();
        var nickName = request.NickName.Trim();
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new AppException(ErrorCodes.BadRequest, "User name is required");
        }

        await EnsureUserNameAvailableAsync(userName, id);

        var affected = await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity
            {
                UserName = userName,
                NickName = nickName,
                UpdatedAt = DateTime.Now
            })
            .Where(x => x.Id == id && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"用户不存在: {id}");
        }

        await permissionCacheInvalidator.RemoveUserAsync(id, cancellationToken);
    }

    public async Task UpdatePasswordAsync(long id, UpdateUserPasswordRequest request, CancellationToken cancellationToken)
    {
        var hashed = passwordHasher.HashPassword(request.Password);
        var affected = await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity
            {
                PasswordHash = hashed.Hash,
                Salt = hashed.Salt,
                UpdatedAt = DateTime.Now
            })
            .Where(x => x.Id == id && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"用户不存在: {id}");
        }

        await permissionCacheInvalidator.RemoveUserAsync(id, cancellationToken);
    }

    public async Task<string> UploadAvatarAsync(long id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "文件不能为空");
        }

        if (file.Length > MaxAvatarFileSizeBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, "文件大小不能超过 10MB");
        }

        var mimeType = file.ContentType.ToLowerInvariant();
        if (!AllowedAvatarMimeTypes.Contains(mimeType))
        {
            throw new AppException(ErrorCodes.BadRequest, $"不支持的文件类型: {mimeType}");
        }

        var exists = await db.Queryable<SysUserEntity>().AnyAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"用户不存在: {id}");
        }

        var ext = Path.GetExtension(file.FileName);
        var storageKey = $"avatar/{DateTime.UtcNow:yyyyMM}/{Guid.NewGuid():N}{ext}";
        var webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var savePath = Path.Combine(webRootPath, storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        await using (var stream = File.Create(savePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var url = $"/{storageKey.Replace('\\', '/')}";
        await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity { AvatarUrl = url, UpdatedAt = DateTime.Now })
            .Where(x => x.Id == id && !x.IsDeleted)
            .ExecuteCommandAsync();

        await permissionCacheInvalidator.RemoveUserAsync(id, cancellationToken);
        return url;
    }

    public async Task UpdateSignatureAsync(long id, UpdateUserSignatureRequest request, CancellationToken cancellationToken)
    {
        var affected = await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity { Signature = request.Signature, UpdatedAt = DateTime.Now })
            .Where(x => x.Id == id && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"用户不存在: {id}");
        }

        await permissionCacheInvalidator.RemoveUserAsync(id, cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, UpdateUserStatusRequest request, CancellationToken cancellationToken)
    {
        var affected = await db.Updateable<SysUserEntity>()
            .SetColumns(x => new SysUserEntity { Status = request.Status, UpdatedAt = DateTime.Now })
            .Where(x => x.Id == id && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"用户不存在: {id}");
        }

        await permissionCacheInvalidator.RemoveUserAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var exists = await db.Queryable<SysUserEntity>().AnyAsync(x => x.Id == id && !x.IsDeleted);
        if (!exists)
        {
            throw new NotFoundException($"用户不存在: {id}");
        }

        await db.Ado.BeginTranAsync();
        try
        {
            await db.Deleteable<SysUserRoleEntity>().Where(x => x.UserId == id).ExecuteCommandAsync();
            await db.Deleteable<SysUserSiteEntity>().Where(x => x.UserId == id).ExecuteCommandAsync();
            await db.Updateable<SysUserEntity>()
                .SetColumns(x => new SysUserEntity { IsDeleted = true, UpdatedAt = DateTime.Now })
                .Where(x => x.Id == id && !x.IsDeleted)
                .ExecuteCommandAsync();
            await db.Ado.CommitTranAsync();
            await permissionCacheInvalidator.RemoveUserAsync(id, cancellationToken);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task EnsureUserNameAvailableAsync(string userName, long? excludeId)
    {
        var query = db.Queryable<SysUserEntity>()
            .Where(x => x.UserName == userName && !x.IsDeleted);

        if (excludeId.HasValue)
        {
            query = query.Where(x => x.Id != excludeId.Value);
        }

        var exists = await query.AnyAsync();
        if (exists)
        {
            throw new ConflictException($"用户名已存在: {userName}");
        }
    }

    private async Task<SysUserEntity> EnsureUserExistsAsync(long id, CancellationToken cancellationToken)
    {
        var user = await db.Queryable<SysUserEntity>().FirstAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (user is null)
        {
            throw new NotFoundException($"用户不存在: {id}");
        }

        return user;
    }

    private async Task<long> GetOrCreateUserGrantRoleAsync(SysUserEntity user)
    {
        var roleCode = $"user_grant_{user.Id}";
        var role = await db.Queryable<SysRoleEntity>().FirstAsync(x => x.RoleCode == roleCode && !x.IsDeleted);
        if (role is not null)
        {
            if (role.Status != 1)
            {
                role.Status = 1;
                role.UpdatedAt = DateTime.Now;
                await db.Updateable(role).ExecuteCommandAsync();
            }

            return role.Id;
        }

        return await db.Insertable(new SysRoleEntity
        {
            RoleName = $"用户授权-{(string.IsNullOrWhiteSpace(user.UserName) ? user.Id.ToString() : user.UserName)}",
            RoleCode = roleCode,
            Status = 1,
            Remark = "用户专属授权角色，由用户授权接口维护",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDeleted = false
        }).ExecuteReturnBigIdentityAsync();
    }

    private static IReadOnlyCollection<UserPermissionTreeNodeDto> BuildUserPermissionTree(IReadOnlyCollection<SysMenuEntity> menus, HashSet<long> grantedMenuIds)
    {
        var nodes = menus.Select(x => new UserPermissionNode
        {
            Value = new UserPermissionTreeNodeDto
            {
                Id = x.Id,
                SiteId = x.SiteId,
                ParentId = x.ParentId,
                MenuName = x.MenuName,
                MenuCode = x.MenuCode,
                PermissionCode = x.PermissionCode,
                MenuType = x.MenuType,
                RoutePath = x.RoutePath,
                Component = x.Component,
                Icon = x.Icon,
                Sort = x.Sort,
                Visible = x.Visible,
                Status = x.Status,
                KeepAlive = x.KeepAlive,
                IsExternal = x.IsExternal,
                Remark = x.Remark,
                Checked = grantedMenuIds.Contains(x.Id),
                Disabled = x.Status != 1
            }
        }).ToList();
        var lookup = nodes.ToDictionary(x => x.Value.Id);

        foreach (var node in lookup.Values)
        {
            if (node.Value.ParentId != 0 && lookup.TryGetValue(node.Value.ParentId, out var parent))
            {
                parent.Children.Add(node);
            }
        }

        return lookup.Values
            .Where(x => x.Value.ParentId == 0 || !lookup.ContainsKey(x.Value.ParentId))
            .OrderBy(x => x.Value.Sort)
            .ThenBy(x => x.Value.Id)
            .Select(x => ToPermissionTreeDto(x, new HashSet<long>()))
            .ToArray();
    }

    private static UserPermissionTreeNodeDto ToPermissionTreeDto(UserPermissionNode node, HashSet<long> ancestors)
    {
        if (!ancestors.Add(node.Value.Id))
        {
            return CopyPermissionTreeNode(node.Value, Array.Empty<UserPermissionTreeNodeDto>());
        }

        var children = node.Children
            .OrderBy(x => x.Value.Sort)
            .ThenBy(x => x.Value.Id)
            .Select(x => ToPermissionTreeDto(x, new HashSet<long>(ancestors)))
            .ToArray();

        return CopyPermissionTreeNode(node.Value, children);
    }

    private static UserPermissionTreeNodeDto CopyPermissionTreeNode(UserPermissionTreeNodeDto node, IReadOnlyCollection<UserPermissionTreeNodeDto> children)
    {
        return new UserPermissionTreeNodeDto
        {
            Id = node.Id,
            SiteId = node.SiteId,
            ParentId = node.ParentId,
            MenuName = node.MenuName,
            MenuCode = node.MenuCode,
            PermissionCode = node.PermissionCode,
            MenuType = node.MenuType,
            RoutePath = node.RoutePath,
            Component = node.Component,
            Icon = node.Icon,
            Sort = node.Sort,
            Visible = node.Visible,
            Status = node.Status,
            KeepAlive = node.KeepAlive,
            IsExternal = node.IsExternal,
            Remark = node.Remark,
            Checked = node.Checked,
            Disabled = node.Disabled,
            Children = children
        };
    }

    private async Task SyncUserRolesAsync(long userId, IReadOnlyCollection<long> roleIds)
    {
        var nextRoleIds = roleIds.Distinct().ToHashSet();
        var userGrantRoleCode = $"user_grant_{userId}";
        var userGrantRoleId = await db.Queryable<SysRoleEntity>()
            .Where(x => x.RoleCode == userGrantRoleCode && !x.IsDeleted)
            .Select(x => x.Id)
            .FirstAsync();
        if (userGrantRoleId > 0)
        {
            var hasUserGrantRole = await db.Queryable<SysUserRoleEntity>()
                .AnyAsync(x => x.UserId == userId && x.RoleId == userGrantRoleId);
            if (hasUserGrantRole)
            {
                nextRoleIds.Add(userGrantRoleId);
            }
        }

        await db.Deleteable<SysUserRoleEntity>().Where(x => x.UserId == userId).ExecuteCommandAsync();
        if (nextRoleIds.Count == 0)
        {
            return;
        }

        await db.Insertable(nextRoleIds.Select(roleId => new SysUserRoleEntity
        {
            UserId = userId,
            RoleId = roleId,
            CreatedAt = DateTime.Now
        }).ToList()).ExecuteCommandAsync();
    }

    private async Task SyncUserSitesAsync(long userId, IReadOnlyCollection<long> siteIds)
    {
        await db.Deleteable<SysUserSiteEntity>().Where(x => x.UserId == userId).ExecuteCommandAsync();
        if (siteIds.Count == 0)
        {
            return;
        }

        await db.Insertable(siteIds.Distinct().Select(siteId => new SysUserSiteEntity
        {
            UserId = userId,
            SiteId = siteId,
            CreatedAt = DateTime.Now
        }).ToList()).ExecuteCommandAsync();
    }

    private sealed class UserPermissionNode
    {
        public required UserPermissionTreeNodeDto Value { get; init; }

        public List<UserPermissionNode> Children { get; } = [];
    }
}
