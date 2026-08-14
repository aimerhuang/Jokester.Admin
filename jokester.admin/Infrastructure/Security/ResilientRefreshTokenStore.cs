using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            if ttl > 0 then
                redis.call('PSETEX', KEYS[2], ttl, value .. '|')
                redis.call('PSETEX', KEYS[3], ARGV[3], 'pending')
            end
            return { 'succeeded', value }
        end

        local consumed = redis.call('GET', KEYS[2])
        if consumed then
            local firstSeparator = string.find(consumed, '|', 1, true)
            local secondSeparator = string.find(consumed, '|', firstSeparator + 1, true)
            local sessionId = string.sub(consumed, firstSeparator + 1, secondSeparator - 1)
            if redis.call('EXISTS', ARGV[1] .. sessionId) == 1 then
                return { 'revoked', consumed }
            end
            local rotation = redis.call('GET', KEYS[3])
            if rotation then
                return { rotation == 'pending' and 'pending' or 'concurrent', consumed, rotation }
            end
            local ttl = redis.call('PTTL', KEYS[2])
            if ttl < 1 then ttl = 1 end
            redis.call('PSETEX', ARGV[1] .. sessionId, ttl, '1')
            local replacementHash = string.sub(consumed, secondSeparator + 1)
            if replacementHash ~= '' then redis.call('DEL', ARGV[2] .. replacementHash) end
            return { 'replayed', consumed }
        end

        return { 'invalid', '' }
        """;

    private const string CompleteRotationScript = """
        local consumed = redis.call('GET', KEYS[1])
        local grace = redis.call('GET', KEYS[2])
        if not consumed or not grace then return 0 end
        local firstSeparator = string.find(consumed, '|', 1, true)
        local secondSeparator = string.find(consumed, '|', firstSeparator + 1, true)
        local sessionId = string.sub(consumed, firstSeparator + 1, secondSeparator - 1)
        if redis.call('EXISTS', ARGV[1] .. sessionId) == 1 then return 0 end
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 1 then return 0 end
        redis.call('PSETEX', KEYS[1], ttl, string.sub(consumed, 1, secondSeparator) .. ARGV[2])
        local graceTtl = redis.call('PTTL', KEYS[2])
        if graceTtl < 1 then return 0 end
        redis.call('PSETEX', KEYS[2], graceTtl, ARGV[3])
        return 1
        """;

    private const string RevokeScript = """
        local value = redis.call('GET', KEYS[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if not value then
            value = redis.call('GET', KEYS[2])
            ttl = redis.call('PTTL', KEYS[2])
        end
        if not value then return 0 end
        local firstSeparator = string.find(value, '|', 1, true)
        local secondSeparator = string.find(value, '|', firstSeparator + 1, true)
        local sessionId = secondSeparator
            and string.sub(value, firstSeparator + 1, secondSeparator - 1)
            or string.sub(value, firstSeparator + 1)
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
    private readonly string _rotationPrefix = $"{redisOptions.Value.InstanceName}refresh_rotation:";
    private readonly string _revokedFamilyPrefix = $"{redisOptions.Value.InstanceName}refresh_family_revoked:";
    private readonly string _userSessionsPrefix = $"{redisOptions.Value.InstanceName}refresh_user_sessions:";
    private readonly bool _enableFallback = redisOptions.Value.EnableInMemoryRefreshTokenFallback;
    private readonly TimeSpan _maximumLifetime = TimeSpan.FromDays(jwtOptions.Value.RefreshTokenExpiresDays);
    private readonly TimeSpan _rotationGrace = TimeSpan.FromSeconds(10);
    private readonly byte[] _rotationEncryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{jwtOptions.Value.SecretKey}|jokester-refresh-rotation-v1"));
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
                [GetTokenKey(tokenHash), GetConsumedKey(tokenHash), GetRotationKey(tokenHash)],
                [_revokedFamilyPrefix, _tokenPrefix, (long)_rotationGrace.TotalMilliseconds]);
            redisAvailable = true;
            var parsed = await ParseConsumeResultAsync(result, tokenHash, cancellationToken);
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
        if (fallbackResult.Status == RefreshTokenConsumeStatus.Concurrent
            && fallbackResult.Tokens is null)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                await Task.Delay(100, cancellationToken);
                fallbackResult = ConsumeFallback(tokenHash);
                if (fallbackResult.Status != RefreshTokenConsumeStatus.Concurrent
                    || fallbackResult.Tokens is not null)
                {
                    break;
                }
            }
        }
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

    public async Task<bool> CompleteRotationAsync(
        string consumedRefreshToken,
        string replacementRefreshToken,
        RefreshTokenRotationTokens tokens,
        CancellationToken cancellationToken)
    {
        var consumedHash = GetHash(consumedRefreshToken);
        var replacementHash = GetHash(replacementRefreshToken);
        var encryptedTokens = EncryptRotationTokens(tokens);
        try
        {
            var completed = (long)await _database.ScriptEvaluateAsync(
                CompleteRotationScript,
                [GetConsumedKey(consumedHash), GetRotationKey(consumedHash)],
                [_revokedFamilyPrefix, replacementHash, encryptedTokens]);
            if (completed == 1)
            {
                CompleteFallbackRotation(consumedHash, replacementHash, tokens);
                return true;
            }
            if (!_enableFallback) return false;
        }
        catch (RedisException ex) when (_enableFallback)
        {
            logger.LogWarning(
                "Redis unavailable when completing refresh-token rotation; using the development in-memory store. FailureType={FailureType}",
                ex.GetType().Name);
        }

        return CompleteFallbackRotation(consumedHash, replacementHash, tokens);
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

                _fallbackConsumedTokens[tokenHash] = entry with { ConsumedAt = now };
                return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Succeeded, entry.UserId, entry.SessionId);
            }

            if (_fallbackConsumedTokens.TryGetValue(tokenHash, out var consumed))
            {
                if (consumed.ConsumedAt.HasValue && now - consumed.ConsumedAt.Value <= _rotationGrace)
                {
                    if (consumed.RotationTokens is not null)
                    {
                        return new RefreshTokenConsumeResult(
                            RefreshTokenConsumeStatus.Concurrent,
                            consumed.UserId,
                            consumed.SessionId,
                            consumed.RotationTokens);
                    }
                    return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Concurrent, consumed.UserId, consumed.SessionId);
                }
                _fallbackRevokedFamilies[consumed.SessionId] = consumed.ExpiresAt;
                if (!string.IsNullOrWhiteSpace(consumed.ReplacementHash))
                {
                    _fallbackTokens.TryRemove(consumed.ReplacementHash, out _);
                }
                return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Replayed, consumed.UserId, consumed.SessionId);
            }

            return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Invalid);
        }
    }

    private bool CompleteFallbackRotation(
        string consumedHash,
        string replacementHash,
        RefreshTokenRotationTokens tokens)
    {
        lock (_fallbackLock)
        {
            var now = DateTime.UtcNow;
            CleanupFallback(now);
            if (!_fallbackConsumedTokens.TryGetValue(consumedHash, out var consumed)
                || !consumed.ConsumedAt.HasValue
                || now - consumed.ConsumedAt.Value > _rotationGrace
                || (_fallbackRevokedFamilies.TryGetValue(consumed.SessionId, out var revokedUntil) && revokedUntil > now))
            {
                return false;
            }
            _fallbackConsumedTokens[consumedHash] = consumed with
            {
                ReplacementHash = replacementHash,
                RotationTokens = tokens
            };
            return true;
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

    private async Task<RefreshTokenConsumeResult> ParseConsumeResultAsync(
        RedisResult[]? values,
        string tokenHash,
        CancellationToken cancellationToken)
    {
        if (values is null || values.Length < 2)
        {
            return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Invalid);
        }

        var rawStatus = values[0].ToString();
        string? encryptedRotation = values.Length >= 3 ? values[2].ToString() : null;
        if (rawStatus == "pending")
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                await Task.Delay(100, cancellationToken);
                var rotation = await _database.StringGetAsync(GetRotationKey(tokenHash));
                if (!rotation.HasValue)
                {
                    var replay = (RedisResult[]?)await _database.ScriptEvaluateAsync(
                        ConsumeScript,
                        [GetTokenKey(tokenHash), GetConsumedKey(tokenHash), GetRotationKey(tokenHash)],
                        [_revokedFamilyPrefix, _tokenPrefix, (long)_rotationGrace.TotalMilliseconds]);
                    return await ParseConsumeResultAsync(replay, tokenHash, cancellationToken);
                }
                if (!string.Equals(rotation.ToString(), "pending", StringComparison.Ordinal))
                {
                    rawStatus = "concurrent";
                    encryptedRotation = rotation.ToString();
                    break;
                }
            }
        }

        var status = rawStatus switch
        {
            "succeeded" => RefreshTokenConsumeStatus.Succeeded,
            "concurrent" => RefreshTokenConsumeStatus.Concurrent,
            "replayed" => RefreshTokenConsumeStatus.Replayed,
            "revoked" => RefreshTokenConsumeStatus.Revoked,
            _ => RefreshTokenConsumeStatus.Invalid
        };
        if (status == RefreshTokenConsumeStatus.Invalid || !TryDeserialize(values[1].ToString(), out var userId, out var sessionId))
        {
            return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Invalid);
        }

        RefreshTokenRotationTokens? tokens = null;
        if (status == RefreshTokenConsumeStatus.Concurrent)
        {
            tokens = DecryptRotationTokens(encryptedRotation);
            if (tokens is null) return new RefreshTokenConsumeResult(RefreshTokenConsumeStatus.Invalid);
        }
        return new RefreshTokenConsumeResult(status, userId, sessionId, tokens);
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
        var secondSeparator = value.IndexOf('|', separator + 1);
        sessionId = secondSeparator < 0 ? value[(separator + 1)..] : value[(separator + 1)..secondSeparator];
        return long.TryParse(value[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out userId);
    }

    private TimeSpan NormalizeTtl(TimeSpan ttl) =>
        ttl <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : ttl > _maximumLifetime ? _maximumLifetime : ttl;

    private string GetTokenKey(string tokenHash) => _tokenPrefix + tokenHash;
    private string GetConsumedKey(string tokenHash) => _consumedPrefix + tokenHash;
    private string GetRotationKey(string tokenHash) => _rotationPrefix + tokenHash;
    private string GetRevokedFamilyKey(string sessionId) => _revokedFamilyPrefix + sessionId;
    private string GetUserSessionsKey(long userId) => _userSessionsPrefix + userId.ToString(CultureInfo.InvariantCulture);

    private static string GetHash(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private static string GetDiagnosticHash(string? value) =>
        string.IsNullOrEmpty(value) ? "unknown" : GetHash(value)[..12];

    private string EncryptRotationTokens(RefreshTokenRotationTokens tokens)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(tokens);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_rotationEncryptionKey, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        return Convert.ToBase64String(payload);
    }

    private RefreshTokenRotationTokens? DecryptRotationTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var payload = Convert.FromBase64String(value);
            if (payload.Length <= 28) return null;
            var plaintext = new byte[payload.Length - 28];
            using var aes = new AesGcm(_rotationEncryptionKey, tagSizeInBytes: 16);
            aes.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), plaintext);
            return JsonSerializer.Deserialize<RefreshTokenRotationTokens>(plaintext);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            logger.LogWarning("Invalid encrypted refresh-token rotation payload was rejected. FailureType={FailureType}", ex.GetType().Name);
            return null;
        }
    }

    private sealed record RefreshTokenEntry(
        long UserId,
        string SessionId,
        DateTime ExpiresAt,
        DateTime? ConsumedAt = null,
        string? ReplacementHash = null,
        RefreshTokenRotationTokens? RotationTokens = null);
}
