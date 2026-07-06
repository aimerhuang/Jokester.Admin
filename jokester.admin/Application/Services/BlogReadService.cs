using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class BlogReadService(ISqlSugarClient db) : IBlogReadService
{
    private const string BlogSiteCode = "blog";
    private const int PublishedStatus = 1;
    private const int ApprovedStatus = 1;

    public async Task<BlogSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var articleQuery = db.Queryable<BlogArticleEntity>()
            .Where(x => x.SiteId == siteId && !x.IsDeleted);
        var commentQuery = db.Queryable<BlogCommentEntity>()
            .Where(x => x.SiteId == siteId && !x.IsDeleted);

        return new BlogSummaryDto
        {
            ArticleCount = await articleQuery.Clone().CountAsync(cancellationToken),
            CommentCount = await commentQuery.Clone().CountAsync(cancellationToken),
            ViewCount = await articleQuery.Clone().SumAsync(x => x.ViewCount)
        };
    }

    public async Task<IReadOnlyCollection<BlogLatestTitleDto>> GetLatestTitlesAsync(int take, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        take = NormalizeTake(take);

        var articles = await db.Queryable<BlogArticleEntity>()
            .Where(x => x.SiteId == siteId && !x.IsDeleted && x.Status == PublishedStatus)
            .OrderByDescending(x => x.CreatedAt)
            .OrderByDescending(x => x.Id)
            .Take(take)
            .Select(x => new BlogLatestTitleDto
            {
                Id = x.Id,
                Title = x.Title,
                CreatedAt = x.CreatedAt,
                CategoryId = x.CategoryId
            })
            .ToListAsync(cancellationToken);

        if (articles.Count == 0)
        {
            return articles;
        }

        var categoryIds = articles.Where(x => x.CategoryId.HasValue).Select(x => x.CategoryId!.Value).Distinct().ToArray();
        if (categoryIds.Length == 0)
        {
            return articles;
        }

        var categories = await db.Queryable<BlogCategoryEntity>()
            .Where(x => x.SiteId == siteId && categoryIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var categoryMap = categories.ToDictionary(x => x.Id, x => x.Name);
        return articles.Select(article => new BlogLatestTitleDto
        {
            Id = article.Id,
            Title = article.Title,
            CreatedAt = article.CreatedAt,
            CategoryId = article.CategoryId,
            CategoryName = article.CategoryId.HasValue && categoryMap.TryGetValue(article.CategoryId.Value, out var name)
                ? name
                : null
        }).ToList();
    }

    public async Task<IReadOnlyCollection<BlogLatestCommentDto>> GetLatestCommentsAsync(int take, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        take = NormalizeTake(take);

        return await db.Queryable<BlogCommentEntity, BlogArticleEntity>((comment, article) => comment.ArticleId == article.Id)
            .Where((comment, article) => comment.SiteId == siteId
                && !comment.IsDeleted
                && comment.Status == ApprovedStatus
                && article.SiteId == siteId
                && !article.IsDeleted
                && article.Status == PublishedStatus)
            .OrderByDescending((comment, article) => comment.CreatedAt)
            .OrderByDescending((comment, article) => comment.Id)
            .Take(take)
            .Select((comment, article) => new BlogLatestCommentDto
            {
                Id = comment.Id,
                ArticleId = comment.ArticleId,
                ArticleTitle = article.Title,
                AuthorName = comment.AuthorName,
                Content = comment.Content,
                CreatedAt = comment.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<BlogSiteInfoDto> GetSiteInfoAsync(CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var buildDate = await db.Queryable<BlogSiteConfigEntity>()
            .Where(x => x.SiteId == siteId && !x.IsDeleted)
            .OrderBy(x => x.Id)
            .Select(x => x.BuildDate)
            .FirstAsync(cancellationToken);

        if (buildDate == default)
        {
            throw new NotFoundException("博客建站时间未配置");
        }

        var summary = await GetSummaryAsync(cancellationToken);
        var buildDateOnly = buildDate.Date;
        var runningDays = Math.Max(1, (DateTime.Today - buildDateOnly).Days + 1);

        return new BlogSiteInfoDto
        {
            BuildDate = buildDateOnly,
            BuildDateText = buildDateOnly.ToString("yyyy年M月d日"),
            RunningDays = runningDays,
            CommentCount = summary.CommentCount,
            ArticleCount = summary.ArticleCount,
            ViewCount = summary.ViewCount
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

    private static int NormalizeTake(int take)
    {
        if (take <= 0)
        {
            return 10;
        }

        return Math.Min(take, 50);
    }
}
