using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using Microsoft.Extensions.Options;

namespace jokester.admin.Infrastructure;

public sealed class AppleAppStoreClient(HttpClient httpClient, IOptions<AppleAppStoreOptions> options) : IAppleAppStoreClient
{
    private readonly AppleAppStoreOptions _options = options.Value;

    public async Task<AppleTransactionLookupResult> GetTransactionAsync(
        string transactionId,
        string environment,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var environments = NormalizeLookupEnvironments(environment);
        foreach (var candidate in environments)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                BuildEndpoint(candidate, transactionId));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                CreateAuthorizationToken());
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound && environments.Count > 1) continue;
            if (!response.IsSuccessStatusCode)
            {
                throw new AppException(
                    response.StatusCode == HttpStatusCode.NotFound ? ErrorCodes.BadRequest : ErrorCodes.ServiceUnavailable,
                    response.StatusCode == HttpStatusCode.NotFound ? MachineErrorCodes.ValidationError : MachineErrorCodes.ServiceUnavailable,
                    response.StatusCode == HttpStatusCode.NotFound
                        ? "Apple transaction does not exist."
                        : "App Store Server API is temporarily unavailable.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("signedTransactionInfo", out var signed)
                || string.IsNullOrWhiteSpace(signed.GetString()))
            {
                throw new AppException(
                    ErrorCodes.ServiceUnavailable,
                    MachineErrorCodes.ServiceUnavailable,
                    "App Store Server API returned an invalid transaction response.");
            }
            return new AppleTransactionLookupResult(signed.GetString()!, candidate);
        }
        throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Apple transaction does not exist.");
    }

    private Uri BuildEndpoint(string environment, string transactionId)
    {
        var baseUrl = string.Equals(environment, "Sandbox", StringComparison.Ordinal)
            ? _options.SandboxBaseUrl
            : _options.ProductionBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "Apple App Store base URL is invalid.");
        }
        return new Uri(baseUri, $"/inApps/v1/transactions/{Uri.EscapeDataString(transactionId)}");
    }

    private string CreateAuthorizationToken()
    {
        using var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(_options.PrivateKeyPem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "Apple private key configuration is invalid.");
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = _options.KeyId, typ = "JWT" }));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = _options.IssuerId,
            iat = now,
            exp = now + 300,
            aud = "appstoreconnect-v1",
            bid = _options.BundleId
        }));
        var signingInput = header + "." + payload;
        var signature = key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return signingInput + "." + Base64UrlEncode(signature);
    }

    private void EnsureConfigured()
    {
        if (!_options.Enabled
            || string.IsNullOrWhiteSpace(_options.BundleId)
            || !Guid.TryParse(_options.IssuerId, out _)
            || string.IsNullOrWhiteSpace(_options.KeyId)
            || string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
        {
            throw new AppException(
                ErrorCodes.ServiceUnavailable,
                MachineErrorCodes.ServiceUnavailable,
                "Apple IAP is not configured.");
        }
    }

    private static IReadOnlyList<string> NormalizeLookupEnvironments(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "production" => ["Production"],
            "sandbox" => ["Sandbox"],
            "both" or "auto" => ["Production", "Sandbox"],
            _ => throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "Apple product environment is invalid.")
        };
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
