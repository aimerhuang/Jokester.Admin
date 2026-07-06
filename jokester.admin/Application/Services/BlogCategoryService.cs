using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class BlogCategoryService(ISqlSugarClient db, ICurrentUser currentUser) : IBlogCategoryService
{
    private const string BlogSiteCode = "blog";

    public async Task<IReadOnlyCollection<BlogCategoryDto>> GetListAsync(CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        return await db.Queryable<BlogCategoryEntity>()
            .Where(x => x.SiteId == siteId && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .Select(x => new BlogCategoryDto
            {
                Id = x.Id,
                SiteId = x.SiteId,
                Name = x.Name,
                Sort = x.Sort,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<long> CreateAsync(SaveBlogCategoryRequest request, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var name = NormalizeName(request.Name);
        await EnsureCategoryNameAvailableAsync(siteId, name, null, cancellationToken);

        var entity = new BlogCategoryEntity
        {
            SiteId = siteId,
            Name = name,
            Sort = request.Sort,
            CreatedAt = DateTime.Now,
            CreatedBy = currentUser.UserId,
            IsDeleted = false
        };

        return await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
    }

    public async Task UpdateAsync(long id, SaveBlogCategoryRequest request, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var entity = await db.Queryable<BlogCategoryEntity>()
            .FirstAsync(x => x.Id == id && x.SiteId == siteId && !x.IsDeleted, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException($"分类不存在: {id}");
        }

        var name = NormalizeName(request.Name);
        await EnsureCategoryNameAvailableAsync(siteId, name, id, cancellationToken);

        entity.Name = name;
        entity.Sort = request.Sort;
        entity.UpdatedAt = DateTime.Now;
        entity.UpdatedBy = currentUser.UserId;

        await db.Updateable(entity).ExecuteCommandAsync();
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var affected = await db.Updateable<BlogCategoryEntity>()
            .SetColumns(x => new BlogCategoryEntity
            {
                IsDeleted = true,
                UpdatedAt = DateTime.Now,
                UpdatedBy = currentUser.UserId
            })
            .Where(x => x.Id == id && x.SiteId == siteId && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"分类不存在: {id}");
        }
    }

    private async Task EnsureCategoryNameAvailableAsync(long siteId, string name, long? currentId, CancellationToken cancellationToken)
    {
        var exists = await db.Queryable<BlogCategoryEntity>()
            .AnyAsync(x => x.SiteId == siteId
                && !x.IsDeleted
                && x.Name == name
                && (!currentId.HasValue || x.Id != currentId.Value), cancellationToken);
        if (exists)
        {
            throw new AppException(ErrorCodes.BadRequest, $"分类名称已存在: {name}");
        }
    }

    private async Task<long> GetBlogSiteIdAsync(CancellationToken cancellationToken)
    {
        var siteId = await db.Queryable<SysSiteEntity>()
            .Where(x => x.SiteCode == BlogSiteCode && !x.IsDeleted)
            .Select(x => x.Id)
            .FirstAsync(cancellationToken);
        if (siteId <= 0)
        {
            throw new NotFoundException($"站点不存在: {BlogSiteCode}");
        }

        return siteId;
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AppException(ErrorCodes.BadRequest, "分类名称不能为空");
        }

        return value.Trim();
    }
}
