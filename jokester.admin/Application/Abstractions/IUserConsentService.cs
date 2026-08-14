using jokester.admin.Application.DTOs.Legal;

namespace jokester.admin.Application.Abstractions;

public interface IUserConsentService
{
    Task<UserConsentsResponse> GetCurrentUserConsentsAsync(CancellationToken cancellationToken);

    Task<ConsentRecordDto> UpdateAiProcessingAsync(UpdateAiProcessingConsentRequest request, CancellationToken cancellationToken);

    Task EnsureAiProcessingConsentAsync(long userId, string providerCode, CancellationToken cancellationToken);
}
