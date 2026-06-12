using jokester.admin.Application.DTOs.Auth;

namespace jokester.admin.Application.Abstractions;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken);

    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);

    Task<UserProfileDto> GetProfileAsync(CancellationToken cancellationToken);

    Task RecordLoginFailureAsync(string? userName, string errorMessage, CancellationToken cancellationToken);
}
