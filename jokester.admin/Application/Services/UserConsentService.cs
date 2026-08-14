using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Legal;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class UserConsentService(
    ISqlSugarClient db,
    ICurrentUser currentUser,
    ILegalDocumentService legalDocumentService) : IUserConsentService
{
    public async Task<UserConsentsResponse> GetCurrentUserConsentsAsync(CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUser();
        var records = await db.Queryable<UserConsentEntity>()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .OrderByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        return new UserConsentsResponse
        {
            PrivacyPolicy = Map(records.FirstOrDefault(x => x.ConsentType == LegalDocumentService.PrivacyPolicyType)),
            TermsOfService = Map(records.FirstOrDefault(x => x.ConsentType == LegalDocumentService.TermsOfServiceType)),
            AiProcessing = Map(records.FirstOrDefault(x => x.ConsentType == LegalDocumentService.AiProcessingType))
        };
    }

    public async Task<ConsentRecordDto> UpdateAiProcessingAsync(
        UpdateAiProcessingConsentRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUser();
        var aiProcessingNotice = RequireAiProcessingNotice(
            await legalDocumentService.GetCurrentAiProcessingNoticeAsync(
                request.ClientPlatform,
                null,
                cancellationToken));
        if (!string.Equals(request.DocumentVersion?.Trim(), aiProcessingNotice.Version, StringComparison.Ordinal))
        {
            throw ConsentRequired(aiProcessingNotice, null);
        }

        var allowedProviders = aiProcessingNotice.ProviderCodes.ToHashSet(StringComparer.Ordinal);
        var providers = request.ProviderCodes
            .Select(LegalDocumentService.NormalizeProviderCode)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (request.Accepted && providers.Length == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "At least one provider code is required.");
        }
        if (providers.Any(x => !allowedProviders.Contains(x)))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "The request contains an unsupported AI provider code.");
        }

        var now = DateTime.UtcNow;
        var entity = new UserConsentEntity
        {
            UserId = userId,
            ConsentType = LegalDocumentService.AiProcessingType,
            DocumentVersion = aiProcessingNotice.Version,
            ProviderCodesJson = providers.Length == 0 ? null : JsonSerializer.Serialize(providers),
            Accepted = request.Accepted,
            ClientPlatform = string.IsNullOrWhiteSpace(request.ClientPlatform) ? "ios" : request.ClientPlatform.Trim().ToLowerInvariant(),
            AcceptedAt = request.Accepted ? now : null,
            RevokedAt = request.Accepted ? null : now,
            CreatedAt = now
        };
        await db.Insertable(entity).ExecuteCommandAsync(cancellationToken);
        return Map(entity)!;
    }

    public async Task EnsureAiProcessingConsentAsync(
        long userId,
        string providerCode,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = LegalDocumentService.NormalizeProviderCode(providerCode);
        var latest = await db.Queryable<UserConsentEntity>()
            .Where(x => x.UserId == userId && x.ConsentType == LegalDocumentService.AiProcessingType)
            .OrderByDescending(x => x.CreatedAt)
            .OrderByDescending(x => x.Id)
            .FirstAsync(cancellationToken);
        var clientPlatform = string.IsNullOrWhiteSpace(latest?.ClientPlatform)
            ? "ios"
            : latest.ClientPlatform;
        var aiProcessingNotice = RequireAiProcessingNotice(
            await legalDocumentService.GetCurrentAiProcessingNoticeAsync(
                clientPlatform,
                null,
                cancellationToken));
        var providers = latest is null ? [] : LegalDocumentService.ParseProviderCodes(latest.ProviderCodesJson);
        if (latest is null
            || !latest.Accepted
            || !string.Equals(latest.DocumentVersion, aiProcessingNotice.Version, StringComparison.Ordinal)
            || !providers.Contains(normalizedProvider, StringComparer.Ordinal))
        {
            throw ConsentRequired(aiProcessingNotice, normalizedProvider);
        }
    }

    private static AiProcessingNoticeDto RequireAiProcessingNotice(AiProcessingNoticeDto? document) =>
        document ?? throw new AppException(
            ErrorCodes.ServiceUnavailable,
            MachineErrorCodes.ServiceUnavailable,
            "AI image generation is unavailable until an approved AI processing notice is configured.");

    private static AppException ConsentRequired(AiProcessingNoticeDto document, string? providerCode) => new(
        ErrorCodes.PreconditionFailed,
        MachineErrorCodes.AiConsentRequired,
        "Current third-party AI data processing consent is required.",
        new
        {
            documentVersion = document.Version,
            documentUrl = document.Url,
            providerCode
        });

    private long RequireCurrentUser() =>
        currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "User is not authenticated.");

    private static ConsentRecordDto? Map(UserConsentEntity? entity) => entity is null ? null : new ConsentRecordDto
    {
        Accepted = entity.Accepted,
        DocumentVersion = entity.DocumentVersion,
        AcceptedAt = entity.AcceptedAt.HasValue ? ApiDateTime.FromUtcStorage(entity.AcceptedAt.Value) : null,
        RevokedAt = entity.RevokedAt.HasValue ? ApiDateTime.FromUtcStorage(entity.RevokedAt.Value) : null,
        ProviderCodes = LegalDocumentService.ParseProviderCodes(entity.ProviderCodesJson),
        ClientPlatform = entity.ClientPlatform
    };
}
