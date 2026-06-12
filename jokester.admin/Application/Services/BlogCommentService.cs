using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class BlogCommentService(
    ISqlSugarClient db,
    ICurrentUser currentUser,
    IBlogCaptchaService captchaService) : IBlogCommentService
{
    private const string BlogSiteCode = "blog";
    private const int PendingStatus = 0;
    private const int ApprovedStatus = 1;
    private const int RejectedStatus = 2;
    private const int SpamStatus = 3;
    private const string DefaultAuthorName = "Anonymous";

    public async Task<PagedResult<BlogCommentDto>> GetPageAsync(
        BlogCommentQuery query, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        RefAsync<int> total = 0;

        var dbQuery = db.Queryable<BlogCommentEntity>()
            .Where(x => !x.IsDeleted && x.SiteId == siteId)
            .WhereIF(query.ArticleId.HasValue, x => x.ArticleId == query.ArticleId!.Value)
            .WhereIF(query.ParentId.HasValue, x => x.ParentId == query.ParentId!.Value)
            .WhereIF(query.Status.HasValue, x => x.Status == query.Status!.Value)
            .WhereIF(!string.IsNullOrWhiteSpace(query.Keyword),
                x => x.AuthorName.Contains(query.Keyword!)
                    || x.Content.Contains(query.Keyword!)
                    || (x.AuthorEmail != null && x.AuthorEmail.Contains(query.Keyword!)))
            .OrderByDescending(x => x.CreatedAt)
            .OrderByDescending(x => x.Id);

        var items = await dbQuery
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
            .ToPageListAsync(query.PageIndex, query.PageSize, total);

        return new PagedResult<BlogCommentDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<PagedResult<PublicBlogCommentDto>> GetPublicPageAsync(
        PublicBlogCommentQuery query, CancellationToken cancellationToken)
    {
        if (query.ArticleId <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "articleId is required");
        }

        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        RefAsync<int> total = 0;
        var dbQuery = db.Queryable<BlogCommentEntity>()
            .Where(x => !x.IsDeleted
                && x.SiteId == siteId
                && x.ArticleId == query.ArticleId
                && x.Status == ApprovedStatus)
            .WhereIF(query.ParentId.HasValue, x => x.ParentId == query.ParentId!.Value)
            .OrderBy(x => x.CreatedAt)
            .OrderBy(x => x.Id);

        var items = await dbQuery
            .Select(x => new PublicBlogCommentDto
            {
                Id = x.Id,
                ArticleId = x.ArticleId,
                ParentId = x.ParentId,
                AuthorName = x.AuthorName,
                AuthorWebsite = x.AuthorWebsite,
                Content = x.Content,
                CreatedAt = x.CreatedAt
            })
            .ToPageListAsync(query.PageIndex, query.PageSize, total);

        return new PagedResult<PublicBlogCommentDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<CreateBlogCommentResult> CreateAsync(
        CreateBlogCommentRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        ValidateCreateRequest(request);
        if (!await captchaService.ValidateAsync(request.CaptchaId, request.CaptchaAnswer, cancellationToken))
        {
            throw new AppException(ErrorCodes.BadRequest, "验证码错误或已过期");
        }

        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var articleExists = await db.Queryable<BlogArticleEntity>()
            .AnyAsync(x => x.Id == request.ArticleId && x.SiteId == siteId && !x.IsDeleted, cancellationToken);
        if (!articleExists)
        {
            throw new NotFoundException($"文章不存在: {request.ArticleId}");
        }

        if (request.ParentId.HasValue)
        {
            var parentExists = await db.Queryable<BlogCommentEntity>()
                .AnyAsync(x => x.Id == request.ParentId.Value
                    && x.SiteId == siteId
                    && x.ArticleId == request.ArticleId
                    && !x.IsDeleted,
                    cancellationToken);
            if (!parentExists)
            {
                throw new NotFoundException($"父评论不存在: {request.ParentId.Value}");
            }
        }

        var entity = new BlogCommentEntity
        {
            SiteId = siteId,
            ArticleId = request.ArticleId,
            ParentId = request.ParentId,
            AuthorName = Truncate(Normalize(request.AuthorName) ?? DefaultAuthorName, 80)!,
            AuthorEmail = Normalize(request.AuthorEmail),
            AuthorWebsite = Normalize(request.AuthorWebsite),
            Content = request.Content.Trim(),
            IpAddress = Normalize(ipAddress),
            UserAgent = Truncate(Normalize(userAgent), 500),
            Status = PendingStatus,
            CreatedAt = DateTime.Now,
            IsDeleted = false
        };

        var id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
        return new CreateBlogCommentResult
        {
            Id = id,
            Status = PendingStatus
        };
    }

    public async Task ReviewAsync(long id, ReviewBlogCommentRequest request, CancellationToken cancellationToken)
    {
        var status = NormalizeReviewStatus(request.Status);
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var affected = await db.Updateable<BlogCommentEntity>()
            .SetColumns(x => new BlogCommentEntity
            {
                Status = status,
                ReviewedAt = DateTime.Now,
                ReviewedBy = currentUser.UserId,
                UpdatedAt = DateTime.Now
            })
            .Where(x => x.Id == id && x.SiteId == siteId && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"评论不存在: {id}");
        }
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var affected = await db.Updateable<BlogCommentEntity>()
            .SetColumns(x => new BlogCommentEntity
            {
                IsDeleted = true,
                UpdatedAt = DateTime.Now
            })
            .Where(x => x.Id == id && x.SiteId == siteId && !x.IsDeleted)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"评论不存在: {id}");
        }
    }

    private static void ValidateCreateRequest(CreateBlogCommentRequest request)
    {
        if (request.ArticleId <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "articleId is required");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new AppException(ErrorCodes.BadRequest, "评论内容不能为空");
        }

        if (string.IsNullOrWhiteSpace(request.CaptchaId) || string.IsNullOrWhiteSpace(request.CaptchaAnswer))
        {
            throw new AppException(ErrorCodes.BadRequest, "验证码不能为空");
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

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return value is { Length: > 0 } && value.Length > maxLength
            ? value[..maxLength]
            : value;
    }

    private static int NormalizeReviewStatus(int status)
    {
        if (status is ApprovedStatus or RejectedStatus or SpamStatus)
        {
            return status;
        }

        throw new AppException(ErrorCodes.BadRequest, "评论审核状态只能是 1、2 或 3");
    }
}
