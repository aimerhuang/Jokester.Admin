using System.Globalization;
using System.Text.Json;
using jokester.admin.Application.Services;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Infrastructure;

public sealed record LegalDocumentConfigurationOptions
{
    public const string SectionName = "LegalDocuments";

    public bool Approved { get; init; }

    public string Platform { get; init; } = string.Empty;

    public string Locale { get; init; } = string.Empty;

    public string EffectiveAt { get; init; } = string.Empty;

    public LegalDocumentDefinitionOptions PrivacyPolicy { get; init; } = new();

    public LegalDocumentDefinitionOptions TermsOfService { get; init; } = new();

    public AiProcessingDocumentDefinitionOptions AiProcessing { get; init; } = new();
}

public class LegalDocumentDefinitionOptions
{
    public string Version { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public bool RequiresReconsent { get; init; }
}

public sealed class AiProcessingDocumentDefinitionOptions : LegalDocumentDefinitionOptions
{
    public bool Enabled { get; init; } = true;

    public string[] ProviderCodes { get; init; } = [];
}

public sealed record LegalDocumentConfigurationResult(
    string Platform,
    string Locale,
    DateTime EffectiveAt,
    IReadOnlyList<string> DocumentTypes,
    IReadOnlyList<string> ProviderCodes);

public static class LegalDocumentConfiguration
{
    private static readonly HashSet<string> SupportedPlatforms =
        new(["ios", "android", "web", "all"], StringComparer.Ordinal);

    private static readonly HashSet<string> SupportedProviderCodes =
        new(["openai", "google"], StringComparer.Ordinal);

    public static async Task<LegalDocumentConfigurationResult> RunAsync(
        ISqlSugarClient db,
        LegalDocumentConfigurationOptions options,
        DateTime? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);

        var now = EnsureUtc(nowUtc ?? DateTime.UtcNow);
        var plan = await BuildPlanAsync(db, options, now, cancellationToken);

        db.Ado.BeginTran();
        try
        {
            foreach (var documentType in plan.DisabledDocumentTypes)
            {
                await db.Updateable<LegalDocumentEntity>()
                    .SetColumns(x => x.Status == 0)
                    .SetColumns(x => x.UpdatedAt == now)
                    .Where(x => x.DocumentType == documentType
                        && x.Platform == plan.Platform
                        && x.Locale == plan.Locale
                        && x.Status == 1)
                    .ExecuteCommandAsync(cancellationToken);
            }

            foreach (var document in plan.Documents)
            {
                await db.Updateable<LegalDocumentEntity>()
                    .SetColumns(x => x.Status == 0)
                    .SetColumns(x => x.UpdatedAt == now)
                    .Where(x => x.DocumentType == document.DocumentType
                        && x.Platform == plan.Platform
                        && x.Locale == plan.Locale
                        && x.Version != document.Version
                        && x.Status == 1)
                    .ExecuteCommandAsync(cancellationToken);

                var existing = await db.Queryable<LegalDocumentEntity>()
                    .Where(x => x.DocumentType == document.DocumentType
                        && x.Platform == plan.Platform
                        && x.Locale == plan.Locale
                        && x.Version == document.Version)
                    .FirstAsync(cancellationToken);
                if (existing is null)
                {
                    document.CreatedAt = now;
                    await db.Insertable(document).ExecuteCommandAsync(cancellationToken);
                }
                else
                {
                    EnsureExistingVersionMatches(existing, document);
                    var affected = await db.Updateable<LegalDocumentEntity>()
                        .SetColumns(x => x.Status == 1)
                        .SetColumns(x => x.UpdatedAt == now)
                        .Where(x => x.Id == existing.Id)
                        .ExecuteCommandAsync(cancellationToken);
                    if (affected != 1)
                    {
                        throw new InvalidOperationException(
                            $"Legal document {document.DocumentType}/{document.Version} was not updated exactly once.");
                    }
                }
            }

            var verificationPlatforms = plan.Platform == "all"
                ? new[] { "ios", "android", "web" }
                : new[] { plan.Platform };
            var verificationLocale = plan.Locale == "all" ? "zh-CN" : plan.Locale;
            foreach (var verificationPlatform in verificationPlatforms)
            {
                var current = await new LegalDocumentService(db)
                    .GetCurrentAsync(verificationPlatform, verificationLocale, cancellationToken);
                var expectedPrivacy = plan.Documents.Single(x => x.DocumentType == LegalDocumentService.PrivacyPolicyType);
                var expectedTerms = plan.Documents.Single(x => x.DocumentType == LegalDocumentService.TermsOfServiceType);
                var expectedAi = plan.Documents.SingleOrDefault(x => x.DocumentType == LegalDocumentService.AiProcessingType);
                var aiMatches = expectedAi is null
                    ? current.AiProcessingNotice is null
                    : current.AiProcessingNotice is not null
                        && string.Equals(current.AiProcessingNotice.Version, expectedAi.Version, StringComparison.Ordinal)
                        && current.AiProcessingNotice.ProviderCodes.SequenceEqual(plan.ProviderCodes, StringComparer.Ordinal);
                if (!string.Equals(current.PrivacyPolicy.Version, expectedPrivacy.Version, StringComparison.Ordinal)
                    || !string.Equals(current.TermsOfService.Version, expectedTerms.Version, StringComparison.Ordinal)
                    || !aiMatches)
                {
                    throw new InvalidOperationException(
                        $"The configured legal documents did not become current for {verificationPlatform}/{verificationLocale}. "
                        + "An active exact-scope document may be overriding the all-platform version.");
                }
            }

            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }

        return new LegalDocumentConfigurationResult(
            plan.Platform,
            plan.Locale,
            plan.EffectiveAt,
            plan.Documents.Select(x => x.DocumentType).ToArray(),
            plan.ProviderCodes);
    }

    private static async Task<ConfigurationPlan> BuildPlanAsync(
        ISqlSugarClient db,
        LegalDocumentConfigurationOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!options.Approved)
        {
            throw new InvalidOperationException(
                "LegalDocuments:Approved must be true only after all document versions and URLs have been approved.");
        }

        var platform = NormalizePlatform(options.Platform);
        var locale = NormalizeLocale(options.Locale);
        var effectiveAt = ParseEffectiveAt(options.EffectiveAt);
        if (effectiveAt > now)
        {
            throw new InvalidOperationException(
                "LegalDocuments:EffectiveAt must be at or before the current UTC time for a current-document configuration.");
        }

        var providerCodes = Array.Empty<string>();
        if (options.AiProcessing.Enabled)
        {
            providerCodes = options.AiProcessing.ProviderCodes
                .Select(LegalDocumentService.NormalizeProviderCode)
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (providerCodes.Length == 0 || providerCodes.Any(x => !SupportedProviderCodes.Contains(x)))
            {
                throw new InvalidOperationException(
                    "LegalDocuments:AiProcessing:ProviderCodes must contain only openai and/or google when AI processing is enabled.");
            }

            var enabledProviders = await db.Queryable<AiImageModelConfigEntity>()
                .Where(x => !x.IsDeleted && x.Status == 1)
                .Select(x => x.Provider)
                .ToListAsync(cancellationToken);
            var requiredProviderCodes = enabledProviders
                .Select(AiImageModelConfigService.ResolveConsentProviderCode)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var missingProviders = requiredProviderCodes.Except(providerCodes, StringComparer.Ordinal).ToArray();
            if (missingProviders.Length > 0)
            {
                throw new InvalidOperationException(
                    "The AI processing notice is missing enabled provider code(s): "
                    + string.Join(", ", missingProviders));
            }
        }

        var documents = new List<LegalDocumentEntity>
        {
            CreateDocument(
                LegalDocumentService.PrivacyPolicyType,
                options.PrivacyPolicy,
                platform,
                locale,
                effectiveAt,
                options.PrivacyPolicy.RequiresReconsent),
            CreateDocument(
                LegalDocumentService.TermsOfServiceType,
                options.TermsOfService,
                platform,
                locale,
                effectiveAt,
                options.TermsOfService.RequiresReconsent)
        };
        if (options.AiProcessing.Enabled)
        {
            documents.Add(CreateDocument(
                LegalDocumentService.AiProcessingType,
                options.AiProcessing,
                platform,
                locale,
                effectiveAt,
                options.AiProcessing.RequiresReconsent,
                JsonSerializer.Serialize(providerCodes)));
        }

        var disabledDocumentTypes = options.AiProcessing.Enabled
            ? Array.Empty<string>()
            : new[] { LegalDocumentService.AiProcessingType };
        return new ConfigurationPlan(
            platform,
            locale,
            effectiveAt,
            providerCodes,
            documents.ToArray(),
            disabledDocumentTypes);
    }

    private static void EnsureExistingVersionMatches(
        LegalDocumentEntity existing,
        LegalDocumentEntity requested)
    {
        var existingProviders = LegalDocumentService.ParseProviderCodes(existing.ProviderCodesJson)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var requestedProviders = LegalDocumentService.ParseProviderCodes(requested.ProviderCodesJson)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!string.Equals(existing.Url, requested.Url, StringComparison.Ordinal)
            || existing.EffectiveAt != requested.EffectiveAt
            || existing.RequiresReconsent != requested.RequiresReconsent
            || !existingProviders.SequenceEqual(requestedProviders, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Legal document {requested.DocumentType}/{requested.Version} already exists with different content. "
                + "Use a new version instead of changing an existing consent target.");
        }
    }

    private static LegalDocumentEntity CreateDocument(
        string documentType,
        LegalDocumentDefinitionOptions options,
        string platform,
        string locale,
        DateTime effectiveAt,
        bool requiresReconsent,
        string? providerCodesJson = null)
    {
        var version = NormalizeVersion(options.Version, documentType);
        var url = NormalizeUrl(options.Url, documentType);
        return new LegalDocumentEntity
        {
            DocumentType = documentType,
            Version = version,
            Platform = platform,
            Locale = locale,
            Url = url,
            ProviderCodesJson = providerCodesJson,
            EffectiveAt = effectiveAt,
            RequiresReconsent = requiresReconsent,
            Status = 1
        };
    }

    private static string NormalizePlatform(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return SupportedPlatforms.Contains(normalized)
            ? normalized
            : throw new InvalidOperationException(
                "LegalDocuments:Platform must be ios, android, web, or all.");
    }

    private static string NormalizeLocale(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 20
            || normalized.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
        {
            throw new InvalidOperationException("LegalDocuments:Locale is invalid.");
        }
        return normalized;
    }

    private static string NormalizeVersion(string? value, string documentType)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 50 || normalized.Any(ch => ch is < '!' or > '~'))
        {
            throw new InvalidOperationException(
                $"LegalDocuments:{documentType}:Version must be a 1-50 character ASCII token.");
        }
        return normalized;
    }

    private static string NormalizeUrl(string? value, string documentType)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 500
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                $"LegalDocuments:{documentType}:Url must be an absolute HTTPS URL without credentials.");
        }
        return uri.AbsoluteUri;
    }

    private static DateTime ParseEffectiveAt(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var hasExplicitOffset = normalized.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            || (normalized.Length >= 6
                && (normalized[^6] is '+' or '-')
                && normalized[^3] == ':');
        if (!hasExplicitOffset
            || !DateTimeOffset.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw new InvalidOperationException(
                "LegalDocuments:EffectiveAt must be an ISO 8601 timestamp with an explicit UTC offset.");
        }
        var utc = parsed.UtcDateTime;
        return new DateTime(
            utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond),
            DateTimeKind.Utc);
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed record ConfigurationPlan(
        string Platform,
        string Locale,
        DateTime EffectiveAt,
        IReadOnlyList<string> ProviderCodes,
        LegalDocumentEntity[] Documents,
        IReadOnlyList<string> DisabledDocumentTypes);
}
