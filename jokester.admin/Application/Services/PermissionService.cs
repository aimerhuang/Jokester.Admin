using jokester.admin.Application.Abstractions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed class PermissionService(
    ISqlSugarClient db,
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> redisOptions,
    ILogger<PermissionService> logger) : IPermissionService
{
    private readonly IDatabase _cache = redis.GetDatabase();
    private readonly string _prefix = $"{redisOptions.Value.InstanceName}perm:";

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(long userId, bool isSuperAdmin, CancellationToken cancellationToken)
    {
        if (isSuperAdmin)
        {
            return await db.Queryable<SysMenuEntity>()
                .Where(x => !x.IsDeleted && x.Status == 1 && x.PermissionCode != null && x.PermissionCode != "")
                .Select(x => x.PermissionCode!)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        var cacheKey = _prefix + userId;
        try
        {
            var cached = await _cache.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                return cached.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }
        catch (RedisConnectionException ex)
        {
            logger.LogWarning(ex, "Redis unavailable when reading permission cache. Falling back to database query.");
        }

        var permissions = await db.Queryable<SysUserRoleEntity, SysRoleEntity, SysRoleMenuEntity, SysMenuEntity>(
                (ur, r, rm, m) => new JoinQueryInfos(
                    JoinType.Inner, ur.RoleId == r.Id,
                    JoinType.Inner, r.Id == rm.RoleId,
                    JoinType.Inner, rm.MenuId == m.Id))
            .Where((ur, r, rm, m) => ur.UserId == userId
                && !r.IsDeleted
                && r.Status == 1
                && !m.IsDeleted
                && m.Status == 1
                && m.PermissionCode != null
                && m.PermissionCode != "")
            .Select((ur, r, rm, m) => m.PermissionCode!)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (permissions.Count > 0)
        {
            try
            {
                await _cache.StringSetAsync(cacheKey, string.Join(",", permissions), TimeSpan.FromHours(2));
            }
            catch (RedisConnectionException ex)
            {
                logger.LogWarning(ex, "Redis unavailable when writing permission cache. Continuing without cache.");
            }
        }

        return permissions;
    }
}
