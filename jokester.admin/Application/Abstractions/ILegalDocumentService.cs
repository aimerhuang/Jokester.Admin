using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Application.DTOs.Legal;

namespace jokester.admin.Application.Abstractions;

public interface ILegalDocumentService
{
    Task<CurrentLegalDocumentsResponse> GetCurrentAsync(string? platform, string? locale, CancellationToken cancellationToken);

    Task<AiProcessingNoticeDto?> GetCurrentAiProcessingNoticeAsync(
        string? platform,
        string? locale,
        CancellationToken cancellationToken);

    Task ValidateAndRecordRegistrationConsentsAsync(long userId, RegisterRequest request, CancellationToken cancellationToken);
}
