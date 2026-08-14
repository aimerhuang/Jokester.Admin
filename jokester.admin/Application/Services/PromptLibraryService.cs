using System.Security.Cryptography;
using System.Text;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Prompts;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed class PromptLibraryService : IPromptLibraryService
{
    private const string ChineseLanguage = "zh-CN";
    private const string LegacyChineseLanguage = "zh";
    private const string DetailViewEvent = "detail_view";
    private const string CopyEvent = "copy";
    private const string UseEvent = "use";
    private const int PromptPreviewLength = 240;
    private const int EventRateLimitPerMinute = 30;
    private const int IpEventRateLimitPerMinute = 120;
    private const string AnonymousSessionCookie = "jokester_prompt_session";
    private const string RateLimitScript = """
        local identity_count = redis.call('INCR', KEYS[1])
        if identity_count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
        local ip_count = redis.call('INCR', KEYS[2])
        if ip_count == 1 then redis.call('EXPIRE', KEYS[2], ARGV[1]) end
        if identity_count > tonumber(ARGV[2]) or ip_count > tonumber(ARGV[3]) then return 0 end
        return 1
        """;

    private readonly ISqlSugarClient db;
    private readonly ICurrentUser currentUser;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IDatabase redis;
    private readonly PromptLibraryOptions options;
    private readonly ILogger<PromptLibraryService> logger;
    private readonly IDataProtector sessionProtector;
    private readonly string redisKeyPrefix;

    public PromptLibraryService(
        ISqlSugarClient db,
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        IConnectionMultiplexer connectionMultiplexer,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<RedisOptions> redisOptions,
        IOptions<PromptLibraryOptions> promptLibraryOptions,
        ILogger<PromptLibraryService> logger)
    {
        this.db = db;
        this.currentUser = currentUser;
        this.httpContextAccessor = httpContextAccessor;
        redis = connectionMultiplexer.GetDatabase();
        options = promptLibraryOptions.Value;
        this.logger = logger;
        sessionProtector = dataProtectionProvider.CreateProtector("Jokester.PromptLibrary.AnonymousSession.v1");
        redisKeyPrefix = $"{redisOptions.Value.InstanceName}:prompt-library";
    }

    public async Task<PagedResult<PromptLibraryListItemDto>> GetPageAsync(
        PromptLibraryQuery query,
        CancellationToken cancellationToken)
    {
        var (keyword, searchField) = ValidateQuery(query);
        RefAsync<int> total = 0;
        var databaseQuery = db.Queryable<PromptLibraryItemEntity>()
            .Where(x => x.Source == options.Source
                && x.IsActive
                && (x.Language == ChineseLanguage || x.Language == LegacyChineseLanguage));

        databaseQuery = searchField switch
        {
            "title" => databaseQuery.WhereIF(keyword is not null, x => x.Title.Contains(keyword!)),
            "prompt" => databaseQuery.WhereIF(keyword is not null, x => x.PromptText.Contains(keyword!)),
            _ => databaseQuery.WhereIF(
                keyword is not null,
                x => x.Title.Contains(keyword!)
                    || x.Description.Contains(keyword!)
                    || x.PromptText.Contains(keyword!))
        };

        var entities = await databaseQuery
            .OrderBy(x => x.SourcePosition)
            .OrderBy(x => x.Id)
            .ToPageListAsync(query.PageIndex, query.PageSize, total);
        cancellationToken.ThrowIfCancellationRequested();

        return new PagedResult<PromptLibraryListItemDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = entities.Select(MapListItem).ToArray()
        };
    }

    public async Task<PromptLibraryDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return null;
        }

        var entity = await db.Queryable<PromptLibraryItemEntity>()
            .FirstAsync(
                x => x.Id == id
                    && x.Source == options.Source
                    && x.IsActive
                    && (x.Language == ChineseLanguage || x.Language == LegacyChineseLanguage),
                cancellationToken);
        return entity is null ? null : MapDetail(entity);
    }

    public async Task<RecordPromptEventResponse> RecordEventAsync(
        long promptId,
        RecordPromptEventRequest request,
        CancellationToken cancellationToken)
    {
        var eventType = NormalizeEventType(request.Type);
        var promptExists = promptId > 0 && await db.Queryable<PromptLibraryItemEntity>()
            .AnyAsync(
                x => x.Id == promptId
                    && x.Source == options.Source
                    && x.IsActive
                    && (x.Language == ChineseLanguage || x.Language == LegacyChineseLanguage),
                cancellationToken);
        if (!promptExists)
        {
            throw new NotFoundException($"Prompt does not exist: {promptId}");
        }

        var day = ResolveMetricDay();
        var identityHash = ResolveIdentityHash();
        await EnforceEventRateLimitAsync(eventType, identityHash, ResolveIpHash(), cancellationToken);
        if (eventType == DetailViewEvent)
        {
            var dedupeKey = $"{redisKeyPrefix}:detail:{day.DateStamp}:{promptId}:{identityHash}";
            var acquired = await TryAcquireDetailViewAsync(dedupeKey, day.DedupeTtl, cancellationToken);
            if (!acquired)
            {
                return new RecordPromptEventResponse { Type = eventType, Recorded = false };
            }

            await IncrementMetricAsync(promptId, day.MetricDate, eventType, cancellationToken);
        }
        else
        {
            await IncrementMetricAsync(promptId, day.MetricDate, eventType, cancellationToken);
        }

        return new RecordPromptEventResponse { Type = eventType, Recorded = true };
    }

    private static (string? Keyword, string SearchField) ValidateQuery(PromptLibraryQuery query)
    {
        if (query.PageIndex < 1)
        {
            throw new AppException(ErrorCodes.BadRequest, "pageIndex must be at least 1");
        }
        if (query.PageSize is < 1 or > 50)
        {
            throw new AppException(ErrorCodes.BadRequest, "pageSize must be between 1 and 50");
        }
        if (query.Keyword?.Length > 100)
        {
            throw new AppException(ErrorCodes.BadRequest, "keyword cannot exceed 100 characters");
        }

        var searchField = (query.SearchField ?? string.Empty).Trim().ToLowerInvariant();
        if (searchField is not ("all" or "title" or "prompt"))
        {
            throw new AppException(ErrorCodes.BadRequest, "searchField must be all, title, or prompt");
        }

        var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();
        return (keyword, searchField);
    }

    private static string NormalizeEventType(string? value)
    {
        var eventType = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (eventType is DetailViewEvent or CopyEvent or UseEvent)
        {
            return eventType;
        }

        throw new AppException(
            ErrorCodes.BadRequest,
            "type must be detail_view, copy, or use; successful generation events are server-managed");
    }

    private async Task<bool> TryAcquireDetailViewAsync(
        RedisKey key,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquired = await redis.StringSetAsync(key, "1", ttl, When.NotExists);
            cancellationToken.ThrowIfCancellationRequested();
            return acquired;
        }
        catch (RedisException ex)
        {
            logger.LogWarning(
                "Prompt detail-view deduplication is unavailable. FailureType={FailureType}",
                ex.GetType().Name);
            throw new AppException(ErrorCodes.ServiceUnavailable, "Prompt event service is unavailable");
        }
    }

    private async Task EnforceEventRateLimitAsync(
        string eventType,
        string identityHash,
        string ipHash,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bucket = now / 60;
        var identityKey = $"{redisKeyPrefix}:rate:identity:{eventType}:{bucket}:{identityHash}";
        var ipKey = $"{redisKeyPrefix}:rate:ip:{eventType}:{bucket}:{ipHash}";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var allowed = (long)await redis.ScriptEvaluateAsync(
                RateLimitScript,
                [identityKey, ipKey],
                [60, EventRateLimitPerMinute, IpEventRateLimitPerMinute]);
            cancellationToken.ThrowIfCancellationRequested();
            if (allowed != 1)
            {
                throw new AppException(ErrorCodes.TooManyRequests, "Too many prompt events");
            }
        }
        catch (RedisException ex)
        {
            logger.LogWarning(
                "Prompt event rate limiting is unavailable. EventType={EventType}, FailureType={FailureType}",
                eventType,
                ex.GetType().Name);
            throw new AppException(ErrorCodes.ServiceUnavailable, "Prompt event service is unavailable");
        }
    }

    private async Task IncrementMetricAsync(
        long promptId,
        DateTime metricDate,
        string eventType,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime;
        var metric = new PromptLibraryMetricDailyEntity
        {
            PromptId = promptId,
            MetricDate = metricDate,
            DetailViewCount = eventType == DetailViewEvent ? 1 : 0,
            CopyCount = eventType == CopyEvent ? 1 : 0,
            UseCount = eventType == UseEvent ? 1 : 0,
            UpdatedAt = now
        };

        // The first event inserts the daily row; duplicates use database-side arithmetic below.
        var inserted = await db.Insertable(metric)
            .MySqlIgnore()
            .ExecuteCommandAsync(cancellationToken);
        if (inserted == 1)
        {
            return;
        }

        var updated = eventType switch
        {
            DetailViewEvent => await db.Updateable<PromptLibraryMetricDailyEntity>()
                .SetColumns(x => x.DetailViewCount == x.DetailViewCount + 1)
                .SetColumns(x => x.UpdatedAt == now)
                .Where(x => x.PromptId == promptId && x.MetricDate == metricDate)
                .ExecuteCommandAsync(cancellationToken),
            CopyEvent => await db.Updateable<PromptLibraryMetricDailyEntity>()
                .SetColumns(x => x.CopyCount == x.CopyCount + 1)
                .SetColumns(x => x.UpdatedAt == now)
                .Where(x => x.PromptId == promptId && x.MetricDate == metricDate)
                .ExecuteCommandAsync(cancellationToken),
            UseEvent => await db.Updateable<PromptLibraryMetricDailyEntity>()
                .SetColumns(x => x.UseCount == x.UseCount + 1)
                .SetColumns(x => x.UpdatedAt == now)
                .Where(x => x.PromptId == promptId && x.MetricDate == metricDate)
                .ExecuteCommandAsync(cancellationToken),
            _ => 0
        };

        if (updated != 1)
        {
            throw new AppException(ErrorCodes.ServerError, "Failed to record prompt event");
        }
    }

    private string ResolveIdentityHash()
    {
        string identity;
        if (currentUser.UserId is { } userId)
        {
            identity = $"user:{userId}";
        }
        else
        {
            var context = httpContextAccessor.HttpContext;
            var protectedSession = context?.Request.Cookies[AnonymousSessionCookie];
            string? session = null;
            if (!string.IsNullOrWhiteSpace(protectedSession))
            {
                try
                {
                    session = sessionProtector.Unprotect(protectedSession);
                    if (session.Length != 32 || session.Any(character => !Uri.IsHexDigit(character)))
                    {
                        session = null;
                    }
                }
                catch (CryptographicException)
                {
                    session = null;
                }
            }

            if (session is null && context is not null && !context.Response.HasStarted)
            {
                session = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                context.Response.Cookies.Append(
                    AnonymousSessionCookie,
                    sessionProtector.Protect(session),
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = context.Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Path = "/api/prompts",
                        MaxAge = TimeSpan.FromDays(365),
                        IsEssential = true
                    });
            }

            if (session is not null)
            {
                identity = "session:" + session;
            }
            else
            {
                var ipAddress = ResolveClientIp();
                var userAgent = context?.Request.Headers.UserAgent.ToString() ?? string.Empty;
                if (userAgent.Length > 512)
                {
                    userAgent = userAgent[..512];
                }
                identity = $"fallback:{ipAddress}\n{userAgent}";
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private string ResolveIpHash()
    {
        var identity = "ip:" + ResolveClientIp();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private string ResolveClientIp() =>
        httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private PromptLibraryListItemDto MapListItem(PromptLibraryItemEntity entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Description = entity.Description,
        PromptPreview = CreatePromptPreview(entity.PromptText),
        CoverImageUrl = BuildCoverImageUrl(entity.CoverLocalPath),
        SourcePosition = entity.SourcePosition
    };

    private PromptLibraryDetailDto MapDetail(PromptLibraryItemEntity entity) => new()
    {
        Id = entity.Id,
        Source = entity.Source,
        ExternalNo = entity.ExternalNo,
        ExternalOccurrence = entity.ExternalOccurrence,
        Title = entity.Title,
        Description = entity.Description,
        PromptText = entity.PromptText,
        CoverImageUrl = BuildCoverImageUrl(entity.CoverLocalPath),
        AuthorName = entity.AuthorName,
        AuthorUrl = entity.AuthorUrl,
        SourceUrl = entity.SourceUrl,
        SourcePublishedAt = entity.SourcePublishedAt.HasValue
            ? ApiDateTime.FromUtcStorage(entity.SourcePublishedAt.Value)
            : null,
        Language = entity.Language,
        SourcePosition = entity.SourcePosition
    };

    private string? BuildCoverImageUrl(string? localPath)
    {
        var publicBasePath = options.PublicBasePath?.Trim();
        if (string.IsNullOrEmpty(publicBasePath)
            || publicBasePath.Contains('?')
            || publicBasePath.Contains('#')
            || string.IsNullOrWhiteSpace(localPath))
        {
            return null;
        }

        var value = localPath.Trim();
        if (value.StartsWith('/')
            || value.StartsWith('\\')
            || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return null;
        }

        var segments = value.Split(['/', '\\'], StringSplitOptions.None);
        if (segments.Length == 0
            || segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.Contains('\0')))
        {
            return null;
        }

        var relativeUrl = string.Join('/', segments.Select(Uri.EscapeDataString));
        var normalizedBasePath = publicBasePath.TrimEnd('/');
        return $"{normalizedBasePath}/{relativeUrl}";
    }

    private static string CreatePromptPreview(string value)
    {
        var normalized = string.Join(
            ' ',
            value.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= PromptPreviewLength
            ? normalized
            : normalized[..PromptPreviewLength] + "...";
    }

    private static MetricDay ResolveMetricDay()
    {
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var nextDay = new DateTimeOffset(now.Date.AddDays(1), TimeSpan.FromHours(8));
        return new MetricDay(
            now.ToString("yyyyMMdd"),
            now.Date,
            nextDay - now + TimeSpan.FromHours(1));
    }

    private readonly record struct MetricDay(string DateStamp, DateTime MetricDate, TimeSpan DedupeTtl);
}
