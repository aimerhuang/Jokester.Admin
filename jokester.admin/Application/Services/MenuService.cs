using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Menus;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using MapsterMapper;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class MenuService(
    ISqlSugarClient db,
    IMapper mapper,
    IPermissionCacheInvalidator permissionCacheInvalidator) : IMenuService
{
    public async Task<IReadOnlyCollection<MenuTreeNodeDto>> GetTreeAsync(long? siteId, CancellationToken cancellationToken)
    {
        var items = await db.Queryable<SysMenuEntity>()
            .Where(x => !x.IsDeleted)
            .WhereIF(siteId.HasValue, x => x.SiteId == siteId!.Value)
            .OrderBy(x => x.SiteId)
            .OrderBy(x => x.ParentId)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var nodes = items.Select(x => mapper.Map<MenuTreeNodeDto>(x)).ToList();
        var lookup = nodes.ToDictionary(x => x.Id, x => new MenuNode { Value = x });

        foreach (var node in lookup.Values)
        {
            if (node.Value.ParentId != 0 && lookup.TryGetValue(node.Value.ParentId, out var parent))
            {
                parent.Children.Add(node);
            }
        }

        var roots = lookup.Values
            .Where(x => x.Value.ParentId == 0 || !lookup.ContainsKey(x.Value.ParentId))
            .OrderBy(x => x.Value.Sort)
            .ThenBy(x => x.Value.Id)
            .ToArray();

        return roots
            .Select(x => ToDto(x, new HashSet<long>()))
            .ToArray();
    }

    public async Task<PagedResult<MenuListItemDto>> GetPageAsync(MenuQuery query, CancellationToken cancellationToken)
    {
        RefAsync<int> total = 0;
        var siteId = query.SiteId;
        var parentId = query.ParentId;
        var menuType = query.MenuType;
        var status = query.Status;
        var dbQuery = db.Queryable<SysMenuEntity>()
            .Where(x => !x.IsDeleted)
            .WhereIF(siteId.HasValue, x => x.SiteId == siteId!.Value)
            .WhereIF(parentId.HasValue, x => x.ParentId == parentId!.Value)
            .WhereIF(!string.IsNullOrWhiteSpace(query.Keyword), x =>
                x.MenuName.Contains(query.Keyword!) ||
                (x.MenuCode != null && x.MenuCode.Contains(query.Keyword!)) ||
                (x.PermissionCode != null && x.PermissionCode.Contains(query.Keyword!)))
            .WhereIF(menuType.HasValue, x => x.MenuType == menuType!.Value)
            .WhereIF(status.HasValue, x => x.Status == status!.Value)
            .OrderBy(x => x.SiteId)
            .OrderBy(x => x.ParentId)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id);

        var items = await dbQuery.Select(x => new MenuListItemDto
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
                Remark = x.Remark
            })
            .ToPageListAsync(query.PageIndex, query.PageSize, total);

        return new PagedResult<MenuListItemDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<long> CreateAsync(SaveMenuRequest request, CancellationToken cancellationToken)
    {
        await EnsureSiteExistsAsync(request.SiteId);
        await EnsureMenuCodeAvailableAsync(request.MenuCode, null);
        await EnsurePermissionCodeAvailableAsync(request.PermissionCode, null);
        await EnsureParentValidAsync(request.SiteId, request.ParentId, 0);

        var entity = mapper.Map<SysMenuEntity>(request);
        entity.IsDeleted = false;
        var id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
        await permissionCacheInvalidator.RemoveAllAsync(cancellationToken);
        return id;
    }

    public async Task UpdateAsync(long id, SaveMenuRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.Queryable<SysMenuEntity>().FirstAsync(x => x.Id == id && !x.IsDeleted);
        if (entity is null)
        {
            throw new NotFoundException($"菜单不存在: {id}");
        }

        await EnsureSiteExistsAsync(request.SiteId);
        await EnsureMenuCodeAvailableAsync(request.MenuCode, id);
        await EnsurePermissionCodeAvailableAsync(request.PermissionCode, id);
        await EnsureParentValidAsync(request.SiteId, request.ParentId, id);

        entity.SiteId = request.SiteId;
        entity.ParentId = request.ParentId;
        entity.MenuName = request.MenuName;
        entity.MenuCode = request.MenuCode;
        entity.MenuType = request.MenuType;
        entity.RoutePath = request.RoutePath;
        entity.Component = request.Component;
        entity.PermissionCode = request.PermissionCode;
        entity.Icon = request.Icon;
        entity.Sort = request.Sort;
        entity.Visible = request.Visible;
        entity.Status = request.Status;
        entity.KeepAlive = request.KeepAlive;
        entity.IsExternal = request.IsExternal;
        entity.Remark = request.Remark;
        await db.Updateable(entity).ExecuteCommandAsync();
        await permissionCacheInvalidator.RemoveAllAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var exists = await db.Queryable<SysMenuEntity>().AnyAsync(x => x.Id == id && !x.IsDeleted);
        if (!exists)
        {
            throw new NotFoundException($"菜单不存在: {id}");
        }

        var childCount = await db.Queryable<SysMenuEntity>().CountAsync(x => x.ParentId == id && !x.IsDeleted);
        if (childCount > 0)
        {
            throw new ConflictException("请先删除子菜单节点");
        }

        await db.Ado.BeginTranAsync();
        try
        {
            await db.Deleteable<SysRoleMenuEntity>().Where(x => x.MenuId == id).ExecuteCommandAsync();
            await db.Updateable<SysMenuEntity>()
                .SetColumns(x => new SysMenuEntity { IsDeleted = true })
                .Where(x => x.Id == id && !x.IsDeleted)
                .ExecuteCommandAsync();
            await db.Ado.CommitTranAsync();
            await permissionCacheInvalidator.RemoveAllAsync(cancellationToken);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task UpdateStatusAsync(long id, UpdateMenuStatusRequest request, CancellationToken cancellationToken)
    {
        var affected = await db.Updateable<SysMenuEntity>()
            .SetColumns(x => new SysMenuEntity { Status = request.Status })
            .Where(x => x.Id == id && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"菜单不存在: {id}");
        }

        await permissionCacheInvalidator.RemoveAllAsync(cancellationToken);
    }

    private async Task EnsureSiteExistsAsync(long siteId)
    {
        var exists = await db.Queryable<SysSiteEntity>().AnyAsync(x => x.Id == siteId && !x.IsDeleted);
        if (!exists)
        {
            throw new NotFoundException($"站点不存在: {siteId}");
        }
    }

    private async Task EnsureParentValidAsync(long siteId, long parentId, long selfId)
    {
        if (parentId == 0)
        {
            return;
        }

        if (parentId == selfId)
        {
            throw new ConflictException("父级菜单不能是自己");
        }

        var exists = await db.Queryable<SysMenuEntity>().AnyAsync(x => x.Id == parentId && x.SiteId == siteId && !x.IsDeleted);
        if (!exists)
        {
            throw new ConflictException("父级菜单不存在或不属于当前站点");
        }
    }

    private async Task EnsureMenuCodeAvailableAsync(string? menuCode, long? excludeId)
    {
        if (string.IsNullOrWhiteSpace(menuCode))
        {
            return;
        }

        var exists = await db.Queryable<SysMenuEntity>()
            .AnyAsync(x => x.MenuCode == menuCode && !x.IsDeleted && (!excludeId.HasValue || x.Id != excludeId.Value));
        if (exists)
        {
            throw new ConflictException($"菜单编码已存在: {menuCode}");
        }
    }

    private async Task EnsurePermissionCodeAvailableAsync(string? permissionCode, long? excludeId)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
        {
            return;
        }

        var exists = await db.Queryable<SysMenuEntity>()
            .AnyAsync(x => x.PermissionCode == permissionCode && !x.IsDeleted && (!excludeId.HasValue || x.Id != excludeId.Value));
        if (exists)
        {
            throw new ConflictException($"权限编码已存在: {permissionCode}");
        }
    }

    private static MenuTreeNodeDto ToDto(MenuNode node, HashSet<long> ancestors)
    {
        if (!ancestors.Add(node.Value.Id))
        {
            // Defensive: break cyclic parent-child references in dirty data
            return new MenuTreeNodeDto
            {
                Id = node.Value.Id,
                SiteId = node.Value.SiteId,
                ParentId = node.Value.ParentId,
                MenuName = node.Value.MenuName,
                MenuCode = node.Value.MenuCode,
                PermissionCode = node.Value.PermissionCode,
                MenuType = node.Value.MenuType,
                RoutePath = node.Value.RoutePath,
                Component = node.Value.Component,
                Icon = node.Value.Icon,
                Sort = node.Value.Sort,
                Visible = node.Value.Visible,
                Status = node.Value.Status,
                KeepAlive = node.Value.KeepAlive,
                IsExternal = node.Value.IsExternal,
                Remark = node.Value.Remark,
                Children = Array.Empty<MenuTreeNodeDto>()
            };
        }

        return new MenuTreeNodeDto
        {
            Id = node.Value.Id,
            SiteId = node.Value.SiteId,
            ParentId = node.Value.ParentId,
            MenuName = node.Value.MenuName,
            MenuCode = node.Value.MenuCode,
            PermissionCode = node.Value.PermissionCode,
            MenuType = node.Value.MenuType,
            RoutePath = node.Value.RoutePath,
            Component = node.Value.Component,
            Icon = node.Value.Icon,
            Sort = node.Value.Sort,
            Visible = node.Value.Visible,
            Status = node.Value.Status,
            KeepAlive = node.Value.KeepAlive,
            IsExternal = node.Value.IsExternal,
            Remark = node.Value.Remark,
            Children = node.Children
                .OrderBy(x => x.Value.Sort)
                .ThenBy(x => x.Value.Id)
                .Select(x => ToDto(x, new HashSet<long>(ancestors)))
                .ToArray()
        };
    }

    private sealed class MenuNode
    {
        public required MenuTreeNodeDto Value { get; init; }

        public List<MenuNode> Children { get; } = [];
    }
}
