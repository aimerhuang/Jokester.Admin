using System.Net;

namespace jokester.admin.Infrastructure;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public string[] AllowedOrigins { get; set; } = [];

    public string[] KnownProxies { get; set; } = [];

    public int PrivateMediaUrlLifetimeMinutes { get; set; } = 10;
}
