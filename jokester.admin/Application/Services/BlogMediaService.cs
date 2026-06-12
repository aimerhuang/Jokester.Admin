using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Blog;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using SqlSugar;
using System.Security.Cryptography;

namespace jokester.admin.Application.Services;

public sealed class BlogMediaService(ISqlSugarClient db, IWebHostEnvironment environment) : IBlogMediaService
{
    private const string BlogSiteCode = "blog";

    private static readonly HashSet<string> AllowedMimeTypes =
    [
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/svg+xml"
    ];

    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    public async Task<UploadMediaResponse> UploadAsync(
        IFormFile file, long uploaderId, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);

        if (file.Length == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "文件不能为空");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, "文件大小不能超过 10MB");
        }

        var mimeType = file.ContentType.ToLowerInvariant();
        if (!AllowedMimeTypes.Contains(mimeType))
        {
            throw new AppException(ErrorCodes.BadRequest, $"不支持的文件类型: {mimeType}");
        }

        var ext = Path.GetExtension(file.FileName);
        var blogPathCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var storageKey = $"blog/{blogPathCode}/{DateTime.UtcNow:yyyyMM}/{Guid.NewGuid():N}{ext}";
        var webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var savePath = Path.Combine(webRootPath, storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        await using (var stream = File.Create(savePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var url = $"/{storageKey.Replace('\\', '/')}";

        var entity = new BlogMediaEntity
        {
            SiteId = siteId,
            FileName = file.FileName,
            StorageKey = storageKey,
            Url = url,
            MimeType = mimeType,
            FileSize = file.Length,
            StorageProvider = "local",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = uploaderId
        };

        entity.Id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();

        return new UploadMediaResponse
        {
            Id = entity.Id,
            Url = url,
            FileName = file.FileName,
            MimeType = mimeType,
            FileSize = file.Length
        };
    }

    public async Task<PagedResult<BlogMediaDto>> GetPageAsync(
        BlogMediaQuery query, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var q = db.Queryable<BlogMediaEntity>()
            .Where(m => !m.IsDeleted && m.SiteId == siteId);

        if (!string.IsNullOrWhiteSpace(query.MimeType))
        {
            q = q.Where(m => m.MimeType.StartsWith(query.MimeType));
        }

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(m => m.CreatedAt)
            .ToPageListAsync(query.PageIndex, query.PageSize);

        return new PagedResult<BlogMediaDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items.Select(m => new BlogMediaDto
            {
                Id = m.Id,
                SiteId = m.SiteId,
                FileName = m.FileName,
                Url = m.Url,
                MimeType = m.MimeType,
                FileSize = m.FileSize,
                Width = m.Width,
                Height = m.Height,
                StorageProvider = m.StorageProvider,
                CreatedAt = m.CreatedAt
            }).ToArray()
        };
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var siteId = await GetBlogSiteIdAsync(cancellationToken);
        var media = await db.Queryable<BlogMediaEntity>()
            .Where(m => m.Id == id && m.SiteId == siteId && !m.IsDeleted)
            .FirstAsync(cancellationToken);

        if (media is null)
        {
            throw new NotFoundException($"媒体资源不存在: {id}");
        }

        await db.Updateable<BlogMediaEntity>()
            .SetColumns(m => new BlogMediaEntity { IsDeleted = true })
            .Where(m => m.Id == id && m.SiteId == siteId)
            .ExecuteCommandAsync();
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
