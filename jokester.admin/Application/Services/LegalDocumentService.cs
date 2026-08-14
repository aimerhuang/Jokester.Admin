using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Auth;
using jokester.admin.Application.DTOs.Legal;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class LegalDocumentService(ISqlSugarClient db) : ILegalDocumentService
{
    public const string PrivacyPolicyType = "privacy_policy";
    public const string TermsOfServiceType = "terms_of_service";
    public const string AiProcessingType = "ai_processing";

    public async Task<CurrentLegalDocumentsResponse> GetCurrentAsync(
        string? platform,
        string? locale,
        CancellationToken cancellationToken)
    {
        var normalizedPlatform = NormalizePlatform(platform);
        var normalizedLocale = NormalizeLocale(locale);
        var now = DateTime.UtcNow;
        var documents = await db.Queryable<LegalDocumentEntity>()
            .Where(x => x.Status == 1 && x.EffectiveAt <= now)
            .Where(x => x.Platform == normalizedPlatform || x.Platform == "all")
            .Where(x => x.Locale == normalizedLocale || x.Locale == "all")
            .OrderByDescending(x => x.EffectiveAt)
            .OrderByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        var privacy = RequireDocument(documents, PrivacyPolicyType, normalizedPlatform, normalizedLocale);
        var terms = RequireDocument(documents, TermsOfServiceType, normalizedPlatform, normalizedLocale);
        var ai = FindDocument(documents, AiProcessingType, normalizedPlatform, normalizedLocale);
        return new CurrentLegalDocumentsResponse
        {
            PrivacyPolicy = MapDocument(privacy),
            TermsOfService = MapDocument(terms),
            AiProcessingNotice = ai is null ? null : MapAiProcessingNotice(ai)
        };
    }

    public async Task<AiProcessingNoticeDto?> GetCurrentAiProcessingNoticeAsync(
        string? platform,
        string? locale,
        CancellationToken cancellationToken)
    {
        var normalizedPlatform = NormalizePlatform(platform);
        var normalizedLocale = NormalizeLocale(locale);
        var now = DateTime.UtcNow;
        var documents = await db.Queryable<LegalDocumentEntity>()
            .Where(x => x.DocumentType == AiProcessingType && x.Status == 1 && x.EffectiveAt <= now)
            .Where(x => x.Platform == normalizedPlatform || x.Platform == "all")
            .Where(x => x.Locale == normalizedLocale || x.Locale == "all")
            .OrderByDescending(x => x.EffectiveAt)
            .OrderByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        var document = FindDocument(documents, AiProcessingType, normalizedPlatform, normalizedLocale);
        return document is null ? null : MapAiProcessingNotice(document);
    }

    public async Task ValidateAndRecordRegistrationConsentsAsync(
        long userId,
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var documents = await GetCurrentAsync(request.ClientPlatform, request.Locale, cancellationToken);
        if (!request.AcceptedPrivacyPolicy
            || !string.Equals(request.PrivacyPolicyVersion?.Trim(), documents.PrivacyPolicy.Version, StringComparison.Ordinal))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Current privacy policy consent is required.");
        }
        if (!request.AcceptedTermsOfService
            || !string.Equals(request.TermsOfServiceVersion?.Trim(), documents.TermsOfService.Version, StringComparison.Ordinal))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Current terms of service consent is required.");
        }

        var now = DateTime.UtcNow;
        await db.Insertable(new[]
        {
            NewConsent(userId, PrivacyPolicyType, documents.PrivacyPolicy.Version, request.ClientPlatform, now),
            NewConsent(userId, TermsOfServiceType, documents.TermsOfService.Version, request.ClientPlatform, now)
        }).ExecuteCommandAsync(cancellationToken);
    }

    public static IReadOnlyList<string> ParseProviderCodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<IReadOnlyList<string>>(json) ?? [])
                .Select(NormalizeProviderCode)
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (JsonException)
        {
            throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "Legal document provider configuration is invalid.");
        }
    }

    public static string NormalizeProviderCode(string? providerCode) =>
        string.IsNullOrWhiteSpace(providerCode) ? string.Empty : providerCode.Trim().ToLowerInvariant();

    private static UserConsentEntity NewConsent(long userId, string type, string version, string platform, DateTime now) => new()
    {
        UserId = userId,
        ConsentType = type,
        DocumentVersion = version,
        Accepted = true,
        ClientPlatform = NormalizePlatform(platform),
        AcceptedAt = now,
        CreatedAt = now
    };

    private static LegalDocumentEntity RequireDocument(
        IEnumerable<LegalDocumentEntity> documents,
        string type,
        string platform,
        string locale)
    {
        return FindDocument(documents, type, platform, locale) ?? throw new AppException(
            ErrorCodes.ServiceUnavailable,
            MachineErrorCodes.ServiceUnavailable,
            $"No active {type} document is configured for {platform}/{locale}.");
    }

    private static LegalDocumentEntity? FindDocument(
        IEnumerable<LegalDocumentEntity> documents,
        string type,
        string platform,
        string locale) => documents
            .Where(x => string.Equals(x.DocumentType, type, StringComparison.Ordinal))
            .OrderByDescending(x => string.Equals(x.Platform, platform, StringComparison.Ordinal))
            .ThenByDescending(x => string.Equals(x.Locale, locale, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.EffectiveAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();

    private static LegalDocumentDto MapDocument(LegalDocumentEntity document) => new()
    {
        Version = document.Version,
        Url = document.Url,
        EffectiveAt = AsUtc(document.EffectiveAt),
        RequiresReconsent = document.RequiresReconsent
    };

    private static AiProcessingNoticeDto MapAiProcessingNotice(LegalDocumentEntity document) => new()
    {
        Version = document.Version,
        Url = document.Url,
        EffectiveAt = AsUtc(document.EffectiveAt),
        RequiresReconsent = document.RequiresReconsent,
        ProviderCodes = ParseProviderCodes(document.ProviderCodesJson)
    };

    private static string NormalizePlatform(string? platform)
    {
        var value = string.IsNullOrWhiteSpace(platform) ? "ios" : platform.Trim().ToLowerInvariant();
        if (value is not ("ios" or "android" or "web" or "all"))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Unsupported client platform.");
        }
        return value;
    }

    private static string NormalizeLocale(string? locale)
    {
        var value = string.IsNullOrWhiteSpace(locale) ? "zh-CN" : locale.Trim();
        if (value.Length > 20 || value.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Invalid locale.");
        }
        return value;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
