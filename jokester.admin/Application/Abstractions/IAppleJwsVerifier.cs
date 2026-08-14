using System.Text.Json;

namespace jokester.admin.Application.Abstractions;

public interface IAppleJwsVerifier
{
    AppleVerifiedJws Verify(string signedPayload);
}

public sealed record AppleVerifiedJws(JsonDocument Payload, string Sha256) : IDisposable
{
    public void Dispose() => Payload.Dispose();
}
