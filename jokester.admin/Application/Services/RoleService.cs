using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Roles;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using MapsterMapper;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class RoleService(
    ISqlSugarClient db,
    IMapper mapper,
    IPermissionCacheInvalidator permissionCacheInvalidator) : IRoleService
{
    public async Task<PagedResult<RoleDto>> GetPageAsync(RoleQuery query, CancellationToken cancellationToken)
    {
        RefAsync<int> total = 0;
        var status = query.Status;
        var dbQuery = db.Queryable<SysRoleEntity>()
            .Where(x => !x.IsDeleted)
            .WhereIF(!string.IsNullOrWhiteSpace(query.Keyword), x => x.RoleName.Contains(query.Keyword!) || x.RoleCode.Contains(query.Keyword!))
            .WhereIF(status.HasValue, x => x.Status == status!.Value)
            .OrderByDescending(x => x.Id);

        var roles = await dbQuery.ToPageListAsync(query.PageIndex, query.PageSize, total);
        if (roles.Count == 0)
        {
            return new PagedResult<RoleDto>
            {
                Total = total,
                PageIndex = query.PageIndex,
                PageSize = query.PageSize
            };
        }

        var roleIds = roles.Select(x => x.Id).ToList();
        var mappings = await db.Queryable<SysRoleMenuEntity>().Where(x => roleIds.Contains(x.RoleId)).ToListAsync();
        var menuLookup = mappings.GroupBy(x => x.RoleId).ToDictionary(x => x.Key, x => (IReadOnlyCollection<long>)x.Select(y => y.MenuId).ToArray());

        var items = roles.Select(x =>
        {
            var dto = mapper.Map<RoleDto>(x);
            return new RoleDto
            {
                Id = dto.Id,
                RoleName = dto.RoleName,
                RoleCode = dto.RoleCode,
                Status = dto.Status,
                Remark = dto.Remark,
                MenuIds = menuLookup.GetValueOrDefault(x.Id, Array.Empty<long>())
            };
        }).ToArray();

        return new PagedResult<RoleDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<long> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken)
    {
        await EnsureRoleCodeAvailableAsync(request.RoleCode, null);
        var entity = mapper.Map<SysRoleEntity>(request);
        entity.CreatedAt = DateTime.Now;
        entity.UpdatedAt = DateTime.Now;
        entity.IsDeleted = false;
        return await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
    }

    public async Task UpdateAsync(long id, SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.Queryable<SysRoleEntity>().FirstAsync(x => x.Id == id && !x.IsDeleted);
        if (entity is null)
        {
            throw new NotFoundException($"角色不存在: {id}");
        }

        await EnsureRoleCodeAvailableAsync(request.RoleCode, id);
        entity.RoleName = request.RoleName;
        entity.RoleCode = request.RoleCode;
        entity.Status = request.Status;
        entity.Remark = request.Remark;
        entity.UpdatedAt = DateTime.Now;
        await db.Updateable(entity).ExecuteCommandAsync();
        await permissionCacheInvalidator.RemoveAllAsync(cancellationToken);
    }

    public async Task AssignMenusAsync(long id, AssignRoleMenusRequest request, CancellationToken cancellationToken)
    {
        var exists = await db.Queryable<SysRoleEntity>().AnyAsync(x => x.Id == id && !x.IsDeleted);
        if (!exists)
        {
            throw new NotFoundException($"角色不存在: {id}");
        }

        await db.Ado.BeginTranAsync();
        try
        {
            await db.Deleteable<SysRoleMenuEntity>().Where(x => x.RoleId == id).ExecuteCommandAsync();
            if (request.MenuIds.Count > 0)
            {
                await db.Insertable(request.MenuIds.Distinct().Select(menuId => new SysRoleMenuEntity
                {
                    RoleId = id,
                    MenuId = menuId,
                    CreatedAt = DateTime.Now
                }).ToList()).ExecuteCommandAsync();
            }

            await db.Ado.CommitTranAsync();
            await permissionCacheInvalidator.RemoveAllAsync(cancellationToken);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task UpdateStatusAsync(long id, UpdateRoleStatusRequest request, CancellationToken cancellationToken)
    {
        var affected = await db.Updateable<SysRoleEntity>()
            .SetColumns(x => new SysRoleEntity
            {
                Status = request.Status,
                UpdatedAt = DateTime.Now
            })
            .Where(x => x.Id == id && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"角色不存在: {id}");
        }

        await permissionCacheInvalidator.RemoveAllAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var role = await db.Queryable<SysRoleEntity>().FirstAsync(x => x.Id == id && !x.IsDeleted);
        if (role is null)
        {
            throw new NotFoundException($"角色不存在: {id}");
        }

        var boundUsers = await db.Queryable<SysUserRoleEntity>().AnyAsync(x => x.RoleId == id);
        if (boundUsers)
        {
            throw new ConflictException("该角色已分配给用户，无法删除");
        }

        await db.Ado.BeginTranAsync();
        try
        {
            await db.Deleteable<SysRoleMenuEntity>().Where(x => x.RoleId == id).ExecuteCommandAsync();
            await db.Updateable<SysRoleEntity>()
                .SetColumns(x => new SysRoleEntity
                {
                    IsDeleted = true,
                    UpdatedAt = DateTime.Now
                })
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

    private async Task EnsureRoleCodeAvailableAsync(string roleCode, long? excludeId)
    {
        var exists = await db.Queryable<SysRoleEntity>()
            .AnyAsync(x => x.RoleCode == roleCode && !x.IsDeleted && (!excludeId.HasValue || x.Id != excludeId.Value));
        if (exists)
        {
            throw new ConflictException($"角色编码已存在: {roleCode}");
        }
    }
}
