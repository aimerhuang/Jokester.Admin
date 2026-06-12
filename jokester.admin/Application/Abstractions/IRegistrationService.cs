using jokester.admin.Application.DTOs.Auth;

namespace jokester.admin.Application.Abstractions;

public interface IRegistrationService
{
    Task SendEmailCodeAsync(SendRegisterEmailCodeRequest request, CancellationToken cancellationToken);

    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
}
