using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;
using System.Text.RegularExpressions;

namespace jokester.admin.Application.Services;

public sealed class BlogArticleService(ISqlSugarClient db, ICurrentUser currentUser) : IBlogArticleService
{
    private const string BlogSiteCode = "blog";
    private const int DraftStatus = 0;
    private const int PublishedStatus = 1;
    private const int HiddenStatus = 2;
    private static readonly Regex ImageSrcRegex = new("<img[^>]+src=[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<PagedResult<BlogArticleDto>> GetPageAsync(BlogArticleQuery query, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        RefAsync<int> total = 0;
        var dbQuery = db.Queryable<BlogArticleEntity>()
            .Where(x => !x.IsDeleted && x.SiteId == siteId)
            .WhereIF(query.Status.HasValue, x => x.Status == query.Status!.Value)
            .WhereIF(!string.IsNullOrWhiteSpace(query.Keyword),
                x => x.Title.Contains(query.Keyword!) || (x.Summary != null && x.Summary.Contains(query.Keyword!)))
            .OrderByDescending(x => x.CreatedAt)
            .OrderByDescending(x => x.Id);

        var items = await dbQuery
            .Select(x => new BlogArticleDto
            {
                Id = x.Id,
                SiteId = x.SiteId,
                Title = x.Title,
                Summary = x.Summary,
                Content = x.Content,
                CoverUrl = x.CoverUrl,
                Status = x.Status
            })
            .ToPageListAsync(query.PageIndex, query.PageSize, total);

        await FillThumbnailUrlsAsync(items, cancellationToken);

        return new PagedResult<BlogArticleDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<BlogArticleDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var article = await db.Queryable<BlogArticleEntity>()
            .Where(x => x.Id == id && x.SiteId == siteId && !x.IsDeleted)
            .Select(x => new BlogArticleDto
            {
                Id = x.Id,
                SiteId = x.SiteId,
                Title = x.Title,
                Summary = x.Summary,
                Content = x.Content,
                CoverUrl = x.CoverUrl,
                Status = x.Status
            })
            .FirstAsync(cancellationToken);

        if (article is not null)
        {
            await FillThumbnailUrlsAsync([article], cancellationToken);
        }

        return article;
    }

    public async Task<long> CreateAsync(SaveBlogArticleRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var status = NormalizeStatus(request.Status);
        var siteId = await GetBlogSiteIdAsync(cancellationToken);

        var entity = new BlogArticleEntity
        {
            SiteId = siteId,
            Title = request.Title.Trim(),
            Summary = Normalize(request.Summary),
            Content = request.Content.Trim(),
            CoverUrl = Normalize(request.CoverUrl),
            Tags = Normalize(request.Tags),
            Status = status,
            ViewCount = 0,
            CreatedAt = DateTime.Now,
            CreatedBy = currentUser.UserId,
            IsDeleted = false
        };

        entity.Id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
        await SyncArticleMediaAsync(entity.Id, entity.Content, cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(long id, SaveBlogArticleRequest request, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var entity = await db.Queryable<BlogArticleEntity>()
            .FirstAsync(x => x.Id == id && x.SiteId == siteId && !x.IsDeleted, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException($"文章不存在: {id}");
        }

        ValidateRequest(request);

        entity.Title = request.Title.Trim();
        entity.Summary = Normalize(request.Summary);
        entity.Content = request.Content.Trim();
        entity.CoverUrl = Normalize(request.CoverUrl);
        entity.Tags = Normalize(request.Tags);
        entity.Status = NormalizeStatus(request.Status);
        entity.UpdatedAt = DateTime.Now;
        entity.UpdatedBy = currentUser.UserId;

        await db.Updateable(entity).ExecuteCommandAsync();
        await SyncArticleMediaAsync(id, entity.Content, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var affected = await db.Updateable<BlogArticleEntity>()
            .SetColumns(x => new BlogArticleEntity
            {
                IsDeleted = true,
                UpdatedAt = DateTime.Now,
                UpdatedBy = currentUser.UserId
            })
            .Where(x => x.Id == id && x.SiteId == siteId && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"文章不存在: {id}");
        }
    }

    public async Task PublishAsync(long id, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var affected = await db.Updateable<BlogArticleEntity>()
            .SetColumns(x => new BlogArticleEntity
            {
                Status = PublishedStatus,
                UpdatedAt = DateTime.Now,
                UpdatedBy = currentUser.UserId
            })
            .Where(x => x.Id == id && x.SiteId == siteId && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"文章不存在: {id}");
        }
    }

    private static void ValidateRequest(SaveBlogArticleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new AppException(ErrorCodes.BadRequest, "文章标题不能为空");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new AppException(ErrorCodes.BadRequest, "文章内容不能为空");
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

    private async Task FillThumbnailUrlsAsync(IReadOnlyCollection<BlogArticleDto> articles, CancellationToken cancellationToken)
    {
        var articleIds = articles.Select(x => x.Id).ToArray();
        if (articleIds.Length == 0)
        {
            return;
        }

        var mediaUrls = await db.Queryable<BlogArticleMediaEntity>()
            .InnerJoin<BlogMediaEntity>((am, m) => am.MediaId == m.Id && !m.IsDeleted)
            .Where(am => articleIds.Contains(am.ArticleId))
            .OrderBy(am => am.Id)
            .Select((am, m) => new { am.ArticleId, m.Url })
            .ToListAsync(cancellationToken);

        var firstMediaUrls = mediaUrls
            .GroupBy(x => x.ArticleId)
            .ToDictionary(x => x.Key, x => x.First().Url);

        foreach (var article in articles)
        {
            article.ThumbnailUrl = Normalize(article.CoverUrl)
                ?? (firstMediaUrls.TryGetValue(article.Id, out var mediaUrl) ? mediaUrl : ExtractFirstImageUrl(article.Content));
        }
    }

    private async Task SyncArticleMediaAsync(long articleId, string content, CancellationToken cancellationToken)
    {
        var imageUrls = ExtractImageUrls(content);
        await db.Deleteable<BlogArticleMediaEntity>()
            .Where(x => x.ArticleId == articleId)
            .ExecuteCommandAsync();

        if (imageUrls.Length == 0)
        {
            return;
        }

        var medias = await db.Queryable<BlogMediaEntity>()
            .Where(x => imageUrls.Contains(x.Url) && !x.IsDeleted)
            .Select(x => new { x.Id, x.Url })
            .ToListAsync(cancellationToken);

        var mediaIds = medias
            .OrderBy(x => Array.IndexOf(imageUrls, x.Url))
            .Select(x => x.Id)
            .Distinct()
            .ToArray();

        if (mediaIds.Length == 0)
        {
            return;
        }

        await db.Insertable(mediaIds.Select(mediaId => new BlogArticleMediaEntity
        {
            ArticleId = articleId,
            MediaId = mediaId,
            CreatedAt = DateTime.Now
        }).ToList()).ExecuteCommandAsync();
    }

    private static string[] ExtractImageUrls(string content)
    {
        return ImageSrcRegex.Matches(content)
            .Select(x => x.Groups["url"].Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
    }

    private static string? ExtractFirstImageUrl(string content)
    {
        return ExtractImageUrls(content).FirstOrDefault();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int NormalizeStatus(int? status)
    {
        if (!status.HasValue)
        {
            return DraftStatus;
        }

        if (status is DraftStatus or PublishedStatus or HiddenStatus)
        {
            return status.Value;
        }

        throw new AppException(ErrorCodes.BadRequest, "文章状态只能是 0、1 或 2");
    }
}
