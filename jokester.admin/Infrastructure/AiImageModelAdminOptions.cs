namespace jokester.admin.Infrastructure;

public sealed class AiImageModelAdminOptions
{
    public const string SectionName = "AiImageModelAdmin";

    /// <summary>
    /// When non-empty, a route base URL host must be one of these hosts.
    /// </summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>
    /// Hosts that may use http and resolve to private, loopback or container-network addresses
    /// (for example a relay running in the same Docker network). Deployment decision only.
    /// </summary>
    public string[] AllowPrivateNetworkHosts { get; set; } = [];

    public int ProbeTimeoutSeconds { get; set; } = 10;
}
