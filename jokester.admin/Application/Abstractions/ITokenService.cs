namespace jokester.admin.Application.Abstractions;

public interface ITokenService
{
    string CreateAccessToken(long userId, string userName, bool isSuperAdmin, string sessionId);

    DateTime GetAccessTokenExpiresAt();

    string CreateRefreshToken();
}
