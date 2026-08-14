namespace jokester.admin.Application.Abstractions;

public interface IRefreshTokenStore
{
    Task<bool> SaveAsync(
        string refreshToken,
        long userId,
        string sessionId,
        DateTime expiresAt,
        CancellationToken cancellationToken);

    /// <summary>Atomically consumes a token and detects reuse of an already-consumed token.</summary>
    Task<RefreshTokenConsumeResult> ConsumeAsync(string refreshToken, CancellationToken cancellationToken);

    Task<bool> CompleteRotationAsync(
        string consumedRefreshToken,
        string replacementRefreshToken,
        RefreshTokenRotationTokens tokens,
        CancellationToken cancellationToken);

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeUserSessionsAsync(long userId, CancellationToken cancellationToken);
}

public enum RefreshTokenConsumeStatus
{
    Invalid = 0,
    Succeeded = 1,
    Concurrent = 2,
    Replayed = 3,
    Revoked = 4
}

public sealed record RefreshTokenConsumeResult(
    RefreshTokenConsumeStatus Status,
    long? UserId = null,
    string? SessionId = null,
    RefreshTokenRotationTokens? Tokens = null);

public sealed record RefreshTokenRotationTokens(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt);
