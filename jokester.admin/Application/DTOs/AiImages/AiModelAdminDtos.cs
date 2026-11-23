namespace jokester.admin.Application.DTOs.AiImages;

/// <summary>
/// Public model status visible to every signed-in user. Never carries endpoints, keys, provider models,
/// route roles, error details, user ids or task ids.
/// </summary>
public sealed class AiImageModelStatusResponse
{
    public DateTime GeneratedAt { get; init; }

    public int WindowHours { get; init; }

    public int SampleLimit { get; init; }

    public IReadOnlyList<AiImageModelStatusItemDto> Items { get; init; } = [];
}

public sealed class AiImageModelStatusItemDto
{
    public string ModelCode { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    /// <summary>normal / degraded / down / unknown</summary>
    public string State { get; init; } = AiImageModelStatusStates.Unknown;

    /// <summary>Rounded success percentage; null when state is unknown.</summary>
    public int? Availability { get; init; }

    public int SampleCount { get; init; }

    public int? FastestSuccessSeconds { get; init; }

    public int? SlowestSuccessSeconds { get; init; }

    /// <summary>Oldest to newest; 1 success, 0 failure.</summary>
    public IReadOnlyList<int> Recent { get; init; } = [];

    /// <summary>UTC completion times aligned with <see cref="Recent"/>.</summary>
    public IReadOnlyList<DateTime> RecentCompletedAt { get; init; } = [];

    public DateTime? LastSampleAt { get; init; }
}

public static class AiImageModelStatusStates
{
    public const string Normal = "normal";
    public const string Degraded = "degraded";
    public const string Down = "down";
    public const string Unknown = "unknown";
}

public sealed class AiImageModelRouteGroupDto
{
    public string ModelCode { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    /// <summary>Current public status state of the model, or null when it is not on the status page.</summary>
    public string? State { get; init; }

    public bool MissingPrimaryRoute { get; init; }

    public IReadOnlyList<AiImageModelRouteDto> Routes { get; init; } = [];
}

/// <summary>
/// Admin view of a route. The API key is write-only: only <see cref="ApiKeySet"/> and a mask are returned.
/// </summary>
public sealed class AiImageModelRouteDto
{
    public long Id { get; init; }

    public int Revision { get; init; }

    public string ModelCode { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string RouteRole { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? ProviderProtocol { get; init; }

    public string ProviderModel { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public bool ApiKeySet { get; init; }

    public string? ApiKeyMask { get; init; }

    public DateTime? ApiKeyUpdatedAt { get; init; }

    public string TextToImagePath { get; init; } = string.Empty;

    public string ImageToImagePath { get; init; } = string.Empty;

    public string RequestFormat { get; init; } = string.Empty;

    public int Sort { get; init; }

    public int Status { get; init; }

    /// <summary>Catalog versions of published, unrevoked size-mode releases that freeze this route.</summary>
    public IReadOnlyList<string> PublishedCatalogVersions { get; init; } = [];

    public DateTime? UpdatedAt { get; init; }

    public string? UpdatedBy { get; init; }
}

public sealed class AiImageModelRouteQuery
{
    public string? Keyword { get; init; }

    public int? Status { get; init; }
}

public sealed class CreateAiImageModelRouteRequest
{
    public string ModelCode { get; init; } = string.Empty;

    public string? ModelName { get; init; }

    public string? ResolutionCode { get; init; }

    public string RouteRole { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string ProviderModel { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string? TextToImagePath { get; init; }

    public string? ImageToImagePath { get; init; }

    public string? RequestFormat { get; init; }

    public int Sort { get; init; }

    public int Status { get; init; } = 1;

    public bool ConfirmOffline { get; init; }
}

public sealed class UpdateAiImageModelRouteRequest
{
    public int Revision { get; init; }

    public string ModelName { get; init; } = string.Empty;

    public string ProviderModel { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Empty or null keeps the current key.</summary>
    public string? ApiKey { get; init; }

    public string? TextToImagePath { get; init; }

    public string? ImageToImagePath { get; init; }

    public string? RequestFormat { get; init; }

    public int Sort { get; init; }
}

public sealed class UpdateAiImageModelRouteStatusRequest
{
    public int Revision { get; init; }

    public int Status { get; init; }

    public bool ConfirmOffline { get; init; }
}

public sealed class AiImageModelRouteProbeResult
{
    /// <summary>passed / auth_failed / unreachable</summary>
    public string Result { get; init; } = string.Empty;

    public int? HttpStatus { get; init; }

    public int ElapsedMilliseconds { get; init; }
}

public static class AiImageModelRouteProbeResults
{
    public const string Passed = "passed";
    public const string AuthFailed = "auth_failed";
    public const string Unreachable = "unreachable";
}

public sealed class AiImageModelRouteAuditQuery
{
    public long? ConfigId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

public sealed class AiImageModelRouteAuditDto
{
    public long Id { get; init; }

    public long ConfigId { get; init; }

    public string ModelCode { get; init; } = string.Empty;

    public string ResolutionCode { get; init; } = string.Empty;

    public string RouteRole { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public IReadOnlyList<string> ChangedFields { get; init; } = [];

    public string? BeforeJson { get; init; }

    public string? AfterJson { get; init; }

    public long OperatorUserId { get; init; }

    public string? OperatorUserName { get; init; }

    public string? OperatorIp { get; init; }

    public DateTime CreatedAt { get; init; }
}
