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

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeUserSessionsAsync(long userId, CancellationToken cancellationToken);
}

public enum RefreshTokenConsumeStatus
{
    Invalid = 0,
    Succeeded = 1,
    Replayed = 2,
    Revoked = 3
}

public sealed record RefreshTokenConsumeResult(
    RefreshTokenConsumeStatus Status,
    long? UserId = null,
    string? SessionId = null);
