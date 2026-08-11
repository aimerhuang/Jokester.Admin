using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using jokester.admin.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace jokester.admin.Infrastructure.Security;

public sealed class ResilientRefreshTokenStore(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<RedisOptions> redisOptions,
    IOptions<JwtOptions> jwtOptions,
    ILogger<ResilientRefreshTokenStore> logger) : IRefreshTokenStore
{
    private const string SaveScript = """
        if redis.call('EXISTS', KEYS[2]) == 1 then return 0 end
        redis.call('PSETEX', KEYS[1], ARGV[3], ARGV[1])
        redis.call('SADD', KEYS[3], ARGV[2])
        local currentTtl = redis.call('PTTL', KEYS[3])
        if currentTtl < tonumber(ARGV[3]) then redis.call('PEXPIRE', KEYS[3], ARGV[3]) end
        return 1
        """;

    private const string ConsumeScript = """
        local value = redis.call('GET', KEYS[1])
        if value then
            local separator = string.find(value, '|', 1, true)
            local sessionId = string.sub(value, separator + 1)
            if redis.call('EXISTS', ARGV[1] .. sessionId) == 1 then
                redis.call('DEL', KEYS[1])
                return { 'revoked', value }
            end
            local ttl = redis.call('PTTL', KEYS[1])
            redis.call('DEL', KEYS[1])
            if ttl > 0 then redis.call('PSETEX', KEYS[2], ttl, value) end
            return { 'succeeded', value }
        end

        local consumed = redis.call('GET', KEYS[2])
        if consumed then
            local separator = string.find(consumed, '|', 1, true)
            local sessionId = string.sub(consumed, separator + 1)
            local ttl = redis.call('PTTL', KEYS[2])
            if ttl < 1 then ttl = 1 end
            redis.call('PSETEX', ARGV[1] .. sessionId, ttl, '1')
            return { 'replayed', consumed }
        end

        return { 'invalid', '' }
        """;

    private const string RevokeScript = """
        local value = redis.call('GET', KEYS[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if not value then
            value = redis.call('GET', KEYS[2])
            ttl = redis.call('PTTL', KEYS[2])
        end
        if not value then return 0 end
        local separator = string.find(value, '|', 1, true)
        local sessionId = string.sub(value, separator + 1)
        if ttl < 1 then ttl = tonumber(ARGV[2]) end
        redis.call('PSETEX', ARGV[1] .. sessionId, ttl, '1')
        redis.call('DEL', KEYS[1])
        return 1
        """;

    private const string RevokeUserSessionsScript = """
        local sessions = redis.call('SMEMBERS', KEYS[1])
        for _, sessionId in ipairs(sessions) do
            redis.call('PSETEX', ARGV[1] .. sessionId, ARGV[2], '1')
        end
        redis.call('DEL', KEYS[1])
        return #sessions
        """;

    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();
    private readonly string _tokenPrefix = $"{redisOptions.Value.InstanceName}refresh_token:";
    private readonly string _consumedPrefix = $"{redisOptions.Value.InstanceName}refresh_consumed:";
    private readonly string _revokedFamilyPrefix = $"{redisOptions.Value.InstanceName}refresh_family_revoked:";
    private readonly string _userSessionsPrefix = $"{redisOptions.Value.InstanceName}refresh_user_sessions:";
    private readonly bool _enableFallback = redisOptions.Value.EnableInMemoryRefreshTokenFallback;
    private readonly TimeSpan _maximumLifetime = TimeSpan.FromDays(jwtOptions.Value.RefreshTokenExpiresDays);
    private readonly ConcurrentDictionary<string, RefreshTokenEntry> _fallbackTokens = new();
    private readonly ConcurrentDictionary<string, RefreshTokenEntry> _fallbackConsumedTokens = new();
    private readonly ConcurrentDictionary<string, DateTime> _fallbackRevokedFamilies = new();
    private readonly object _fallbackLock = new();

    public async Task<bool> SaveAsync(
        string refreshToken,
        long userId,
        string sessionId,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        var ttl = NormalizeTtl(expiresAt - DateTime.UtcNow);
        var tokenHash = GetHash(refreshToken);
        var value = Serialize(userId, sessionId);

        try
        {
            var saved = (long)await _database.ScriptEvaluateAsync(
                SaveScript,
                [GetTokenKey(tokenHash), GetRevokedFamilyKey(sessionId), GetUserSessionsKey(userId)],
                [value, sessionId, (long)ttl.TotalMilliseconds]);
            if (saved == 1)
            {
                RemoveFallbackToken(tokenHash);
                return true;
            }

            return false;
        }
        catch (RedisException ex) when (_enableFallback)
        {
            logger.LogWarning(
                "Redis unavailable when saving refresh token; using the development in-memory store. FailureType={FailureType}",
                ex.GetType().Name);
            return SaveFallback(tokenHash, new RefreshTokenEntry(userId, sessionId, expiresAt));
        }
    }

    public async Task<RefreshTokenConsumeResult> ConsumeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = GetHash(refreshToken);
        var redisAvailable = false;
        try
        {
            var result = (RedisResult[]?)await _database.ScriptEvaluateAsync(
                ConsumeScript,
                [GetTokenKey(tokenHash), GetConsumedKey(tokenHash)],
                [_revokedFamilyPrefix]);
            redisAvailable = true;
            var parsed = ParseConsumeResult(result);
            if (parsed.Status != RefreshTokenConsumeStatus.Invalid || !_enableFallback)
            {
                if (parsed.Status == RefreshTokenConsumeStatus.Replayed)
                {
                    LogReplay(parsed);
                }
                return parsed;
            }
        }
        catch (RedisException ex) when (_enableFallback)
        {
            logger.LogWarning(
                "Redis unavailable when consuming refresh token; using the development in-memory store. FailureType={FailureType}",
                ex.GetType().Name);
        }

        var fallbackResult = ConsumeFallback(tokenHash);
        if (fallbackResult.Status == RefreshTokenConsumeStatus.Replayed)
        {
            LogReplay(fallbackResult);
            if (redisAvailable && !string.IsNullOrWhiteSpace(fallbackResult.SessionId))
            {
                try
                {
                    await _database.StringSetAsync(
                        GetRevokedFamilyKey(fallbackResult.SessionId),
                        "1",
                        _maximumLifetime);
                }
                catch (RedisException ex)
                {
                    logger.LogWarning(
                        "Redis became unavailable while propagating a fallback token replay. FailureType={FailureType}",
                        ex.GetType().Name);
                }
            }
        }
        return fallbackResult;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = GetHash(refreshToken);
        try
        {
            await _database.ScriptEvaluateAsync(
                RevokeScript,
                [GetTokenKey(tokenHash), GetConsumedKey(tokenHash)],
                [_revokedFamilyPrefix, (long)_maximumLifetime.TotalMilliseconds]);
        }
        catch (RedisException ex) when (_enableFallback)
        {
            logger.LogWarning(
                "Redis unavailable when revoking refresh token; revoking the development in-memory session only. FailureType={FailureType}",
                ex.GetType().Name);
        }

        RevokeFallback(tokenHash);
    }

    public async Task RevokeUserSessionsAsync(long userId, CancellationToken cancellationToken)
    {
        try
        {
            await _database.ScriptEvaluateAsync(
                RevokeUserSessionsScript,
                [GetUserSessionsKey(userId)],
                [_revokedFamilyPrefix, (long)_maximumLifetime.TotalMilliseconds]);
        }
        catch (RedisException ex) when (_enableFallback)
        {
            logger.LogWarning(
                "Redis unavailable when revoking user sessions; revoking development in-memory sessions only. UserId={UserId}, FailureType={FailureType}",
                userId,
                ex.GetType().Name);
        }

        lock (_fallbackLock)
        {
            CleanupFallback(DateTime.UtcNow);
            foreach (var entry in _fallbackTokens.Values.Concat(_fallbackConsumedTokens.Values).Where(x => x.UserId == userId))
            {
                _fallbackRevokedFamilies[entry.SessionId] = entry.ExpiresAt;
            }

            foreach (var pair in _fallbackTokens.Where(x => x.Value.UserId == userId).ToArray())
            {
                _fallbackTokens.TryRemove(pair.Key, out _);
            }
        }
    }

    private bool SaveFallback(string tokenHash, RefreshTokenEntry entry)
    {
        lock (_fallbackLock)
        {
            CleanupFallback(DateTime.UtcNow);
            if (_fallbackRevokedFamilies.TryGetValue(entry.SessionId, out var revokedUntil)
                && revokedUntil > DateTime.UtcNow)
            {
                return false;
            }

            _fallbackTokens[tokenHash] = entry;
            _fallbackConsumedTokens.TryRemove(tokenHash, out _);
            return true;
        }
    }

    private RefreshTokenConsumeResult ConsumeFallback(string tokenHash)
    {
        lock (_fallbackLock)
        {
            var now = DateTime.UtcNow;
            CleanupFallback(now);
            if (_fallbackTokens.TryRemove(tokenHash, out var entry))
            {
                if (_fallbackRevokedFamilies.TryGetValue(entry.SessionId, out var revokedUntil) && revokedUntil > now)
                {
                    return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Revoked, entry.UserId, entry.SessionId);
                }

                _fallbackConsumedTokens[tokenHash] = entry;
                return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Succeeded, entry.UserId, entry.SessionId);
            }

            if (_fallbackConsumedTokens.TryGetValue(tokenHash, out var consumed))
            {
                _fallbackRevokedFamilies[consumed.SessionId] = consumed.ExpiresAt;
                return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Replayed, consumed.UserId, consumed.SessionId);
            }

            return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Invalid);
        }
    }

    private void RevokeFallback(string tokenHash)
    {
        lock (_fallbackLock)
        {
            CleanupFallback(DateTime.UtcNow);
            if (_fallbackTokens.TryRemove(tokenHash, out var active)
                || _fallbackConsumedTokens.TryGetValue(tokenHash, out active))
            {
                _fallbackRevokedFamilies[active.SessionId] = active.ExpiresAt;
            }
        }
    }

    private void RemoveFallbackToken(string tokenHash)
    {
        lock (_fallbackLock)
        {
            _fallbackTokens.TryRemove(tokenHash, out _);
            _fallbackConsumedTokens.TryRemove(tokenHash, out _);
        }
    }

    private void CleanupFallback(DateTime now)
    {
        foreach (var pair in _fallbackTokens.Where(x => x.Value.ExpiresAt <= now).ToArray())
        {
            _fallbackTokens.TryRemove(pair.Key, out _);
        }
        foreach (var pair in _fallbackConsumedTokens.Where(x => x.Value.ExpiresAt <= now).ToArray())
        {
            _fallbackConsumedTokens.TryRemove(pair.Key, out _);
        }
        foreach (var pair in _fallbackRevokedFamilies.Where(x => x.Value <= now).ToArray())
        {
            _fallbackRevokedFamilies.TryRemove(pair.Key, out _);
        }
    }

    private void LogReplay(RefreshTokenConsumeResult result)
    {
        logger.LogWarning(
            "Refresh token replay detected; the token family was revoked. UserId={UserId}, SessionIdHash={SessionIdHash}",
            result.UserId,
            GetDiagnosticHash(result.SessionId));
    }

    private static RefreshTokenConsumeResult ParseConsumeResult(RedisResult[]? values)
    {
        if (values is null || values.Length < 2)
        {
            return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Invalid);
        }

        var status = values[0].ToString() switch
        {
            "succeeded" => RefreshTokenConsumeStatus.Succeeded,
            "replayed" => RefreshTokenConsumeStatus.Replayed,
            "revoked" => RefreshTokenConsumeStatus.Revoked,
            _ => RefreshTokenConsumeStatus.Invalid
        };
        if (status == RefreshTokenConsumeStatus.Invalid || !TryDeserialize(values[1].ToString(), out var userId, out var sessionId))
        {
            return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Invalid);
        }

        return new RefreshTokenConsumeResult(status, userId, sessionId);
    }

    private static string Serialize(long userId, string sessionId) =>
        $"{userId.ToString(CultureInfo.InvariantCulture)}|{sessionId}";

    private static bool TryDeserialize(string? value, out long userId, out string sessionId)
    {
        userId = 0;
        sessionId = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var separator = value.IndexOf('|');
        if (separator <= 0 || separator == value.Length - 1) return false;
        sessionId = value[(separator + 1)..];
        return long.TryParse(value[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out userId);
    }

    private TimeSpan NormalizeTtl(TimeSpan ttl) =>
        ttl <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : ttl > _maximumLifetime ? _maximumLifetime : ttl;

    private string GetTokenKey(string tokenHash) => _tokenPrefix + tokenHash;
    private string GetConsumedKey(string tokenHash) => _consumedPrefix + tokenHash;
    private string GetRevokedFamilyKey(string sessionId) => _revokedFamilyPrefix + sessionId;
    private string GetUserSessionsKey(long userId) => _userSessionsPrefix + userId.ToString(CultureInfo.InvariantCulture);

    private static string GetHash(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private static string GetDiagnosticHash(string? value) =>
        string.IsNullOrEmpty(value) ? "unknown" : GetHash(value)[..12];

    private sealed record RefreshTokenEntry(long UserId, string SessionId, DateTime ExpiresAt);
}
