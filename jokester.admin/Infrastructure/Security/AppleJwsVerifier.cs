using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace jokester.admin.Infrastructure.Security;

public sealed class AppleJwsVerifier : IAppleJwsVerifier, IDisposable
{
    private readonly IReadOnlyList<X509Certificate2> _trustedRoots;

    public AppleJwsVerifier(IOptions<AppleAppStoreOptions> options, IHostEnvironment environment)
    {
        var paths = new List<string>
        {
            Path.Combine(environment.ContentRootPath, "certificates", "apple", "AppleRootCA-G3.pem")
        };
        paths.AddRange(options.Value.TrustedRootCertificatePaths ?? []);
        _trustedRoots = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.IsPathFullyQualified(path) ? path : Path.Combine(environment.ContentRootPath, path))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Where(File.Exists)
            .Select(X509CertificateLoader.LoadCertificateFromFile)
            .ToArray();
    }

    public AppleVerifiedJws Verify(string signedPayload)
    {
        if (string.IsNullOrWhiteSpace(signedPayload) || signedPayload.Length > 2_000_000)
        {
            throw InvalidPayload();
        }

        var parts = signedPayload.Split('.');
        if (parts.Length != 3) throw InvalidPayload();
        JsonDocument? header = null;
        JsonDocument? payload = null;
        var certificates = new List<X509Certificate2>();
        try
        {
            header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            var root = header.RootElement;
            if (!root.TryGetProperty("alg", out var algorithm)
                || !string.Equals(algorithm.GetString(), "ES256", StringComparison.Ordinal)
                || !root.TryGetProperty("x5c", out var chainElement)
                || chainElement.ValueKind != JsonValueKind.Array
                || chainElement.GetArrayLength() < 2
                || chainElement.GetArrayLength() > 6)
            {
                throw InvalidPayload();
            }

            foreach (var certificate in chainElement.EnumerateArray())
            {
                var encoded = certificate.GetString();
                if (string.IsNullOrWhiteSpace(encoded)) throw InvalidPayload();
                certificates.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(encoded)));
            }
            ValidateCertificateChain(certificates);

            var signature = Base64UrlDecode(parts[2]);
            if (signature.Length != 64) throw InvalidPayload();
            using var key = certificates[0].GetECDsaPublicKey() ?? throw InvalidPayload();
            var signedBytes = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            if (!key.VerifyData(
                    signedBytes,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                throw InvalidPayload();
            }

            payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            return new AppleVerifiedJws(
                payload,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signedPayload))));
        }
        catch (AppException)
        {
            payload?.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            payload?.Dispose();
            throw InvalidPayload();
        }
        finally
        {
            header?.Dispose();
            foreach (var certificate in certificates) certificate.Dispose();
        }
    }

    private void ValidateCertificateChain(IReadOnlyList<X509Certificate2> certificates)
    {
        if (_trustedRoots.Count == 0)
        {
            throw new AppException(
                ErrorCodes.ServiceUnavailable,
                MachineErrorCodes.ServiceUnavailable,
                "Apple trust roots are not configured.");
        }

        var now = DateTime.UtcNow;
        if (certificates.Any(certificate => now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime()))
        {
            throw InvalidPayload();
        }
        var usage = certificates[0].Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (usage is not null && !usage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
        {
            throw InvalidPayload();
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.VerificationTime = now;
        foreach (var root in _trustedRoots) chain.ChainPolicy.CustomTrustStore.Add(root);
        foreach (var intermediate in certificates.Skip(1)) chain.ChainPolicy.ExtraStore.Add(intermediate);
        if (!chain.Build(certificates[0])) throw InvalidPayload();

        var chainRoot = chain.ChainElements[^1].Certificate;
        if (!_trustedRoots.Any(root => string.Equals(root.Thumbprint, chainRoot.Thumbprint, StringComparison.OrdinalIgnoreCase)))
        {
            throw InvalidPayload();
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch { 2 => "==", 3 => "=", 0 => string.Empty, _ => throw InvalidPayload() };
        return Convert.FromBase64String(normalized);
    }

    private static AppException InvalidPayload() => new(
        ErrorCodes.BadRequest,
        MachineErrorCodes.ValidationError,
        "Apple signed payload verification failed.");

    public void Dispose()
    {
        foreach (var certificate in _trustedRoots) certificate.Dispose();
    }
}
