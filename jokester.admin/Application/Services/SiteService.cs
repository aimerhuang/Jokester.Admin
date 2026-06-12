using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Sites;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using MapsterMapper;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class SiteService(ISqlSugarClient db, IMapper mapper) : ISiteService
{
    public async Task<PagedResult<SiteDto>> GetPageAsync(SiteQuery query, CancellationToken cancellationToken)
    {
        RefAsync<int> total = 0;
        var status = query.Status;
        var dbQuery = db.Queryable<SysSiteEntity>()
            .Where(x => !x.IsDeleted)
            .WhereIF(!string.IsNullOrWhiteSpace(query.Keyword), x => x.SiteName.Contains(query.Keyword!) || x.SiteCode.Contains(query.Keyword!))
            .WhereIF(status.HasValue, x => x.Status == status!.Value)
            .OrderBy(x => x.Sort)
            .OrderByDescending(x => x.Id);

        var items = await dbQuery.Select(x => new SiteDto
            {
                Id = x.Id,
                SiteName = x.SiteName,
                SiteCode = x.SiteCode,
                Domain = x.Domain,
                Status = x.Status,
                Description = x.Description,
                Sort = x.Sort
            })
            .ToPageListAsync(query.PageIndex, query.PageSize, total);

        return new PagedResult<SiteDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<IReadOnlyList<SiteDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await db.Queryable<SysSiteEntity>()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .OrderByDescending(x => x.Id)
            .Select(x => new SiteDto
            {
                Id = x.Id,
                SiteName = x.SiteName,
                SiteCode = x.SiteCode,
                Domain = x.Domain,
                Status = x.Status,
                Description = x.Description,
                Sort = x.Sort
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<long> CreateAsync(SaveSiteRequest request, CancellationToken cancellationToken)
    {
        await EnsureSiteCodeAvailableAsync(request.SiteCode, null, cancellationToken);

        var entity = mapper.Map<SysSiteEntity>(request);
        entity.IsDeleted = false;
        return await db.Insertable(entity).ExecuteReturnSnowflakeIdAsync();
    }

    public async Task UpdateAsync(long id, SaveSiteRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.Queryable<SysSiteEntity>().FirstAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException($"站点不存在: {id}");
        }

        await EnsureSiteCodeAvailableAsync(request.SiteCode, id, cancellationToken);

        entity.SiteName = request.SiteName;
        entity.SiteCode = request.SiteCode;
        entity.Domain = request.Domain;
        entity.Description = request.Description;
        entity.Status = request.Status;
        entity.Sort = request.Sort;
        await db.Updateable(entity).ExecuteCommandAsync();
    }

    public async Task UpdateStatusAsync(long id, UpdateSiteStatusRequest request, CancellationToken cancellationToken)
    {
        var affected = await db.Updateable<SysSiteEntity>()
            .SetColumns(x => new SysSiteEntity { Status = request.Status })
            .Where(x => x.Id == id && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"站点不存在: {id}");
        }
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var site = await db.Queryable<SysSiteEntity>().FirstAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (site is null)
        {
            throw new NotFoundException($"站点不存在: {id}");
        }

        var hasMenus = await db.Queryable<SysMenuEntity>().AnyAsync(x => x.SiteId == id && !x.IsDeleted, cancellationToken);
        if (hasMenus)
        {
            throw new ConflictException("该站点下仍有菜单，无法删除");
        }

        var hasUserBindings = await db.Queryable<SysUserSiteEntity>().AnyAsync(x => x.SiteId == id, cancellationToken);
        if (hasUserBindings)
        {
            throw new ConflictException("该站点已分配给用户，无法删除");
        }

        await db.Updateable<SysSiteEntity>()
            .SetColumns(x => new SysSiteEntity { IsDeleted = true })
            .Where(x => x.Id == id && !x.IsDeleted)
            .ExecuteCommandAsync();
    }

    private async Task EnsureSiteCodeAvailableAsync(string siteCode, long? excludeId, CancellationToken cancellationToken)
    {
        var exists = await db.Queryable<SysSiteEntity>()
            .AnyAsync(x => x.SiteCode == siteCode && !x.IsDeleted && (!excludeId.HasValue || x.Id != excludeId.Value), cancellationToken);
        if (exists)
        {
            throw new ConflictException($"站点编码已存在: {siteCode}");
        }
    }
}
