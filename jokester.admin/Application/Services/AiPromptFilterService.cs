using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Models.AiPromptFilter;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Application.Services;

public sealed class AiPromptFilterService(
    IServiceScopeFactory scopeFactory,
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> redisOptions,
    IOptions<AiPromptFilterOptions> filterOptions,
    ILogger<AiPromptFilterService> logger) : IAiPromptFilter
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private AiPromptMatcherSnapshot? _snapshot;
    private long _lastVerifiedUtcTicks;

    public long CurrentRevision => Volatile.Read(ref _snapshot)?.Revision ?? 0;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (!filterOptions.Value.Enabled || Volatile.Read(ref _snapshot) is not null)
        {
            return;
        }

        await RefreshAsync(force: false, publish: false, cancellationToken);
    }

    public async Task<AiPromptFilterResult> CheckAsync(string? text, CancellationToken cancellationToken)
    {
        var snapshot = filterOptions.Value.Enabled
            ? await GetFreshSnapshotAsync(cancellationToken)
            : null;
        return CheckWithSnapshot(snapshot, text);
    }

    public async Task<AiPromptFilterResult> EnsureAllowedAsync(string? text, string fieldName, CancellationToken cancellationToken)
    {
        var result = await CheckAsync(text, cancellationToken);
        if (!result.IsAllowed)
        {
            throw new AiPromptRejectedException(fieldName, result);
        }

        return result;
    }

    public async Task<long> EnsureAllAllowedAsync(
        IReadOnlyList<AiPromptFilterText> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var snapshot = filterOptions.Value.Enabled
            ? await GetFreshSnapshotAsync(cancellationToken)
            : null;
        var revision = snapshot?.Revision ?? CurrentRevision;
        for (var index = 0; index < texts.Count; index++)
        {
            var item = texts[index];
            var match = snapshot?.Find(item.Text);
            if (match is not null && match.Action == AiPromptFilterActions.Block)
            {
                throw new AiPromptRejectedException(
                    item.FieldName,
                    new AiPromptFilterResult(false, revision, match));
            }
        }

        return revision;
    }

    public async Task<long> RefreshAsync(bool force, bool publish, CancellationToken cancellationToken)
    {
        if (!filterOptions.Value.Enabled)
        {
            return 0;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            AiPromptSensitiveWordRevisionEntity revisionEntity;
            AiPromptFilterRule[] rules;
            await db.Ado.BeginTranAsync();
            try
            {
                revisionEntity = await db.Queryable<AiPromptSensitiveWordRevisionEntity>()
                    .Where(x => x.Id == 1)
                    .FirstAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Prompt filter revision row does not exist");

                var current = Volatile.Read(ref _snapshot);
                if (!force && current is not null && current.Revision == revisionEntity.Revision)
                {
                    await db.Ado.CommitTranAsync();
                    MarkVerified();
                    return current.Revision;
                }

                var entities = await db.Queryable<AiPromptSensitiveWordEntity>()
                    .Where(x => !x.IsDeleted && x.Status == 1)
                    .OrderByDescending(x => x.Severity)
                    .OrderBy(x => x.Id)
                    .ToListAsync(cancellationToken);
                rules = entities.Select(MapRule).ToArray();
                await db.Ado.CommitTranAsync();
            }
            catch
            {
                await db.Ado.RollbackTranAsync();
                throw;
            }

            var activeBlockRuleCount = rules.Count(x => x.Action == AiPromptFilterActions.Block);
            if (activeBlockRuleCount < filterOptions.Value.MinimumActiveWordCount)
            {
                throw new InvalidOperationException(
                    $"Prompt filter requires at least {filterOptions.Value.MinimumActiveWordCount} active block rules");
            }

            var snapshot = new AiPromptMatcherSnapshot(revisionEntity.Revision, DateTime.UtcNow, rules);
            Volatile.Write(ref _snapshot, snapshot);
            MarkVerified();

            logger.LogInformation(
                "Prompt filter snapshot loaded. Revision={Revision}, RuleCount={RuleCount}",
                snapshot.Revision,
                snapshot.RuleCount);

            if (publish)
            {
                await PublishRevisionAsync(snapshot.Revision);
            }

            return snapshot.Revision;
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                "Prompt filter snapshot load failed. FailureType={FailureType}",
                ex.GetType().Name);
            throw new AiPromptFilterUnavailableException();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task PublishRevisionAsync(long revision)
    {
        try
        {
            var database = redis.GetDatabase();
            var subscriber = redis.GetSubscriber();
            var revisionKey = BuildRedisKey("revision");
            var channel = RedisChannel.Literal(BuildRedisKey("changed"));
            await database.StringSetAsync(revisionKey, revision);
            await subscriber.PublishAsync(channel, revision, CommandFlags.FireAndForget);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Prompt filter revision publish failed. Revision={Revision}, FailureType={FailureType}",
                revision,
                ex.GetType().Name);
        }
    }

    private string BuildRedisKey(string suffix) => $"{redisOptions.Value.InstanceName}ai-prompt-filter:{suffix}";

    private async Task<AiPromptMatcherSnapshot> GetFreshSnapshotAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        var snapshot = Volatile.Read(ref _snapshot)
            ?? throw new AiPromptFilterUnavailableException();

        if (GetSnapshotAge() <= TimeSpan.FromMinutes(filterOptions.Value.MaxSnapshotAgeMinutes))
        {
            return snapshot;
        }

        try
        {
            await RefreshAsync(force: false, publish: false, cancellationToken);
            snapshot = Volatile.Read(ref _snapshot) ?? snapshot;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                "Prompt filter snapshot refresh failed after the maximum age. Revision={Revision}, FailureType={FailureType}",
                snapshot.Revision,
                ex.GetType().Name);
            throw new AiPromptFilterUnavailableException();
        }

        return GetSnapshotAge() <= TimeSpan.FromMinutes(filterOptions.Value.MaxSnapshotAgeMinutes)
            ? snapshot
            : throw new AiPromptFilterUnavailableException();
    }

    private TimeSpan GetSnapshotAge()
    {
        var ticks = Volatile.Read(ref _lastVerifiedUtcTicks);
        return ticks <= 0 ? TimeSpan.MaxValue : DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
    }

    private void MarkVerified()
    {
        Volatile.Write(ref _lastVerifiedUtcTicks, DateTime.UtcNow.Ticks);
    }

    private AiPromptFilterResult CheckWithSnapshot(
        AiPromptMatcherSnapshot? snapshot,
        string? text)
    {
        var revision = snapshot?.Revision ?? CurrentRevision;
        var lexicalMatch = snapshot?.Find(text);
        if (lexicalMatch is not null && lexicalMatch.Action == AiPromptFilterActions.Block)
        {
            return new AiPromptFilterResult(false, revision, lexicalMatch);
        }

        return new AiPromptFilterResult(true, revision, lexicalMatch);
    }

    private static AiPromptFilterRule MapRule(AiPromptSensitiveWordEntity entity)
    {
        var matchMode = entity.MatchMode.Trim().ToLowerInvariant();
        if (!AiPromptFilterMatchModes.IsSupported(matchMode))
        {
            throw new InvalidOperationException($"Unsupported prompt filter match mode on rule {entity.Id}");
        }

        var action = entity.Action.Trim().ToLowerInvariant();
        if (!AiPromptFilterActions.IsSupported(action))
        {
            throw new InvalidOperationException($"Unsupported prompt filter action on rule {entity.Id}");
        }

        var normalizedTerm = AiPromptTextNormalizer.NormalizeRuleTerm(entity.Term, matchMode);
        if (string.IsNullOrEmpty(normalizedTerm))
        {
            throw new InvalidOperationException($"Prompt filter rule {entity.Id} normalizes to an empty value");
        }

        return new AiPromptFilterRule(
            entity.Id,
            entity.Term,
            normalizedTerm,
            entity.LanguageCode,
            entity.CategoryCode,
            matchMode,
            action,
            entity.Severity);
    }
}
