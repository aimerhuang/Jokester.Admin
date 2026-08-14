using jokester.admin.Application.DTOs.Auth;

namespace jokester.admin.Application.Abstractions;

public interface IAccountDeletionService
{
    Task<AccountDeletionRequestDto> CreateAsync(CreateAccountDeletionRequest request, CancellationToken cancellationToken);

    Task<AccountDeletionRequestDto?> GetCurrentAsync(CancellationToken cancellationToken);

    Task<AccountDeletionRequestDto> CancelAsync(string requestId, CancellationToken cancellationToken);

    Task ProcessDueRequestsAsync(CancellationToken cancellationToken);
}
