using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class BlogDashboardService(ISqlSugarClient db) : IBlogDashboardService
{
    private const string BlogSiteCode = "blog";

    public async Task<BlogDashboardStatsDto> GetStatsAsync(CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var today = DateTime.Today;

        var articleQuery = db.Queryable<BlogArticleEntity>()
            .Where(x => x.SiteId == siteId && !x.IsDeleted);
        var commentQuery = db.Queryable<BlogCommentEntity>()
            .Where(x => x.SiteId == siteId && !x.IsDeleted);
        var mediaQuery = db.Queryable<BlogMediaEntity>()
            .Where(x => x.SiteId == siteId && !x.IsDeleted);

        var recentPending = await commentQuery.Clone()
            .Where(x => x.Status == 0)
            .OrderByDescending(x => x.CreatedAt)
            .OrderByDescending(x => x.Id)
            .Take(10)
            .Select(x => new BlogCommentDto
            {
                Id = x.Id,
                ArticleId = x.ArticleId,
                ParentId = x.ParentId,
                AuthorName = x.AuthorName,
                AuthorEmail = x.AuthorEmail,
                AuthorWebsite = x.AuthorWebsite,
                Content = x.Content,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                ReviewedAt = x.ReviewedAt,
                ReviewedBy = x.ReviewedBy
            })
            .ToListAsync(cancellationToken);

        return new BlogDashboardStatsDto
        {
            Articles = new BlogArticleStatsDto
            {
                Total = await articleQuery.Clone().CountAsync(cancellationToken),
                Draft = await articleQuery.Clone().CountAsync(x => x.Status == 0, cancellationToken),
                Published = await articleQuery.Clone().CountAsync(x => x.Status == 1, cancellationToken),
                Hidden = await articleQuery.Clone().CountAsync(x => x.Status == 2, cancellationToken),
                ViewCount = await articleQuery.Clone().SumAsync(x => x.ViewCount)
            },
            Comments = new BlogCommentStatsDto
            {
                Total = await commentQuery.Clone().CountAsync(cancellationToken),
                Pending = await commentQuery.Clone().CountAsync(x => x.Status == 0, cancellationToken),
                Approved = await commentQuery.Clone().CountAsync(x => x.Status == 1, cancellationToken),
                Rejected = await commentQuery.Clone().CountAsync(x => x.Status == 2, cancellationToken),
                Spam = await commentQuery.Clone().CountAsync(x => x.Status == 3, cancellationToken),
                Today = await commentQuery.Clone().CountAsync(x => x.CreatedAt >= today, cancellationToken)
            },
            Media = new BlogMediaStatsDto
            {
                Total = await mediaQuery.Clone().CountAsync(cancellationToken),
                TotalBytes = await mediaQuery.Clone().SumAsync(x => x.FileSize)
            },
            RecentPendingComments = recentPending
        };
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
}
