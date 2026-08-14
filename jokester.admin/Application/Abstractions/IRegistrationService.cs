using jokester.admin.Application.DTOs.Auth;

namespace jokester.admin.Application.Abstractions;

public interface IRegistrationService
{
    Task<SendRegisterEmailCodeResponse> SendEmailCodeAsync(
        SendRegisterEmailCodeRequest request,
        CancellationToken cancellationToken);

    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
}
