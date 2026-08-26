using System.Net;
using System.Net.Sockets;

namespace jokester.admin.Infrastructure;

public static class OutboundNetworkGuard
{
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None))
        {
            return false;
        }
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 when bytes[1] is >= 64 and <= 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] == 168 => false,
                198 when bytes[1] is 18 or 19 => false,
                >= 224 => false,
                _ => !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
                    && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                    && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
            };
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }
        return !address.IsIPv6LinkLocal
            && !address.IsIPv6Multicast
            && !address.IsIPv6SiteLocal
            && (bytes[0] & 0xfe) != 0xfc;
    }

    public static async ValueTask<Stream> ConnectPublicAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        Exception? lastError = null;
        foreach (var address in addresses.Where(IsPublicAddress))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastError = ex;
                if (ex is OperationCanceledException)
                {
                    throw;
                }
            }
        }
        throw new HttpRequestException("Outbound endpoint did not resolve to an approved public address.", lastError);
    }
}
