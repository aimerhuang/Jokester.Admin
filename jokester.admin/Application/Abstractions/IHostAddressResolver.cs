using System.Net;

namespace jokester.admin.Application.Abstractions;

public interface IHostAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}
