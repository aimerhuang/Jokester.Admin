namespace jokester.admin.Infrastructure;

public sealed class EmailValidationOptions
{
    public const string SectionName = "EmailValidation";

    public bool EnableApiValidation { get; set; }

    public string ApiEndpoint { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string ApiKeyHeaderName { get; set; } = "Authorization";

    public IReadOnlyCollection<string> BlacklistDomains { get; set; } = Array.Empty<string>();
}
