using System.Net;
using System.Net.Sockets;
using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Infrastructure;

namespace jokester.admin.Application.Services;

public sealed class DnsHostAddressResolver : IHostAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        IPAddress.TryParse(host, out var literal)
            ? Task.FromResult(new[] { literal })
            : Dns.GetHostAddressesAsync(host, cancellationToken);
}

/// <summary>
/// SSRF guard for provider routes edited from the admin UI. A saved base URL is requested by the server,
/// so it must be https, carry no credentials/query/fragment, and must not resolve to private, loopback,
/// link-local (cloud metadata) or reserved addresses unless the host is explicitly allowed by deployment config.
/// </summary>
public static class AiImageRouteEndpointPolicy
{
    public const int MaxBaseUrlLength = 500;
    public const int MaxPathLength = 200;

    public static Uri NormalizeBaseUrl(string? rawBaseUrl, AiImageModelAdminOptions options)
    {
        var value = rawBaseUrl?.Trim() ?? string.Empty;
        if (value.Length == 0 || value.Length > MaxBaseUrlLength)
        {
            throw Invalid("baseUrl is required and must be at most 500 characters.");
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            throw Invalid("baseUrl must be an absolute https URL.");
        }
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || value.Contains('?') || value.Contains('#'))
        {
            throw Invalid("baseUrl must not contain credentials, a query string or a fragment.");
        }

        var host = uri.IdnHost;
        var privateAllowed = IsPrivateNetworkHostAllowed(host, options);
        if (uri.Scheme != Uri.UriSchemeHttps && !privateAllowed)
        {
            throw Invalid("baseUrl must use https.");
        }
        if (options.AllowedHosts.Length > 0
            && !privateAllowed
            && !options.AllowedHosts.Any(x => string.Equals(x.Trim(), host, StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid("baseUrl host is not in AiImageModelAdmin.AllowedHosts.");
        }
        if (!privateAllowed && IPAddress.TryParse(host, out var literal) && IsBlockedAddress(literal))
        {
            throw Invalid("baseUrl must not point to a private, loopback or link-local address.");
        }

        var normalized = value.TrimEnd('/');
        return new Uri(normalized, UriKind.Absolute);
    }

    public static async Task EnsureResolvesToPublicAddressesAsync(
        Uri baseUrl,
        AiImageModelAdminOptions options,
        IHostAddressResolver resolver,
        CancellationToken cancellationToken)
    {
        var host = baseUrl.IdnHost;
        if (IsPrivateNetworkHostAllowed(host, options))
        {
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await resolver.ResolveAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            throw Invalid("baseUrl host could not be resolved.");
        }

        if (addresses.Length == 0)
        {
            throw Invalid("baseUrl host could not be resolved.");
        }
        if (addresses.Any(IsBlockedAddress))
        {
            throw Invalid("baseUrl must not resolve to a private, loopback or link-local address.");
        }
    }

    public static string NormalizePath(string? rawPath, string defaultPath)
    {
        var value = string.IsNullOrWhiteSpace(rawPath) ? defaultPath : rawPath.Trim();
        if (value.Length > MaxPathLength
            || !value.StartsWith('/')
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains('?')
            || value.Contains('#')
            || value.Contains('\\')
            || value.Contains("://", StringComparison.Ordinal)
            || value.Any(char.IsWhiteSpace)
            || value.Any(char.IsControl))
        {
            throw Invalid("Endpoint paths must start with '/' and must not contain '..', '//', '?', '#' or a full URL.");
        }
        return value;
    }

    /// <summary>
    /// Handler for server-side requests to admin-entered endpoints. It resolves the host itself and refuses to
    /// connect to blocked addresses, so a DNS answer that changes after validation (rebinding) is still rejected.
    /// Redirects are not followed.
    /// </summary>
    public static SocketsHttpHandler CreateGuardedHandler(AiImageModelAdminOptions options, IHostAddressResolver resolver) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(options.ProbeTimeoutSeconds),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = await resolver.ResolveAsync(host, cancellationToken);
            if (!IsPrivateNetworkHostAllowed(host, options) && (addresses.Length == 0 || addresses.Any(IsBlockedAddress)))
            {
                throw new HttpRequestException("The endpoint resolves to a blocked address.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

    public static bool IsPrivateNetworkHostAllowed(string host, AiImageModelAdminOptions options) =>
        options.AllowPrivateNetworkHosts.Any(x => string.Equals(x.Trim(), host, StringComparison.OrdinalIgnoreCase));

    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0
                || b[0] == 10
                || b[0] == 127
                || b[0] == 100 && b[1] is >= 64 and <= 127
                || b[0] == 169 && b[1] == 254
                || b[0] == 172 && b[1] is >= 16 and <= 31
                || b[0] == 192 && b[1] == 0 && b[2] == 0
                || b[0] == 192 && b[1] == 168
                || b[0] == 198 && b[1] is 18 or 19
                || b[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(address)
                || address.Equals(IPAddress.IPv6None)
                || address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast)
            {
                return true;
            }
            var b = address.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC;
        }

        return true;
    }

    private static AppException Invalid(string message) =>
        new(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, message);
}
