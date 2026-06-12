namespace jokester.admin.Application.Abstractions;

public interface IRefreshTokenStore
{
    Task SaveAsync(string refreshToken, long userId, DateTime expiresAt, CancellationToken cancellationToken);

    Task<long?> GetUserIdAsync(string refreshToken, CancellationToken cancellationToken);

    Task RemoveAsync(string refreshToken, CancellationToken cancellationToken);
}
