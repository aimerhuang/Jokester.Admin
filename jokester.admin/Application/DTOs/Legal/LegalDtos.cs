using System.Text.Json.Serialization;

namespace jokester.admin.Application.DTOs.Legal;

public class LegalDocumentDto
{
    public string Version { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public DateTime EffectiveAt { get; init; }

    public bool RequiresReconsent { get; init; }
}

public sealed class AiProcessingNoticeDto : LegalDocumentDto
{
    public IReadOnlyList<string> ProviderCodes { get; init; } = [];
}

public sealed class CurrentLegalDocumentsResponse
{
    public LegalDocumentDto PrivacyPolicy { get; init; } = new();

    public LegalDocumentDto TermsOfService { get; init; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public AiProcessingNoticeDto? AiProcessingNotice { get; init; }
}

public sealed class ConsentRecordDto
{
    public bool Accepted { get; init; }

    public string DocumentVersion { get; init; } = string.Empty;

    public DateTime? AcceptedAt { get; init; }

    public DateTime? RevokedAt { get; init; }

    public IReadOnlyList<string> ProviderCodes { get; init; } = [];

    public string ClientPlatform { get; init; } = string.Empty;
}

public sealed class UserConsentsResponse
{
    public ConsentRecordDto? PrivacyPolicy { get; init; }

    public ConsentRecordDto? TermsOfService { get; init; }

    public ConsentRecordDto? AiProcessing { get; init; }
}

public sealed class UpdateAiProcessingConsentRequest
{
    public bool Accepted { get; init; }

    public string DocumentVersion { get; init; } = string.Empty;

    public IReadOnlyList<string> ProviderCodes { get; init; } = [];

    public string ClientPlatform { get; init; } = "ios";
}
