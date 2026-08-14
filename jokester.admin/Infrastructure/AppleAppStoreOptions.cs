using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace jokester.admin.Infrastructure;

public sealed class AppleAppStoreOptions
{
    public const string SectionName = "AppleAppStore";

    public bool Enabled { get; set; }

    public string BundleId { get; set; } = string.Empty;

    public string IssuerId { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public string PrivateKeyPem { get; set; } = string.Empty;

    public string AppAccountTokenKey { get; set; } = string.Empty;

    public string ProductionBaseUrl { get; set; } = "https://api.storekit.itunes.apple.com";

    public string SandboxBaseUrl { get; set; } = "https://api.storekit-sandbox.itunes.apple.com";

    public string Environment { get; set; } = "Production";

    public string[] TrustedRootCertificatePaths { get; set; } = [];
}

public sealed class AppleAppStoreOptionsValidator : IValidateOptions<AppleAppStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, AppleAppStoreOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;

        var errors = new List<string>();
        if (!IsValidBundleId(options.BundleId)) errors.Add("AppleAppStore:BundleId is invalid.");
        if (!Guid.TryParse(options.IssuerId, out _)) errors.Add("AppleAppStore:IssuerId must be a UUID.");
        if (string.IsNullOrWhiteSpace(options.KeyId) || options.KeyId.Length > 128)
            errors.Add("AppleAppStore:KeyId is invalid.");
        if (Encoding.UTF8.GetByteCount(options.AppAccountTokenKey ?? string.Empty) < 32)
            errors.Add("AppleAppStore:AppAccountTokenKey must be at least 32 bytes.");
        if (!IsHttpsUrl(options.ProductionBaseUrl))
            errors.Add("AppleAppStore:ProductionBaseUrl must be an absolute HTTPS URL.");
        if (!IsHttpsUrl(options.SandboxBaseUrl))
            errors.Add("AppleAppStore:SandboxBaseUrl must be an absolute HTTPS URL.");
        if (options.Environment is not ("Production" or "Sandbox" or "Both"))
            errors.Add("AppleAppStore:Environment must be Production, Sandbox, or Both.");
        if (!IsValidPrivateKey(options.PrivateKeyPem))
            errors.Add("AppleAppStore:PrivateKeyPem must contain a valid P-256 EC private key.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsValidBundleId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 200
        && value.Contains('.', StringComparison.Ordinal)
        && value.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-');

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(uri.Host);

    private static bool IsValidPrivateKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(value);
            return key.KeySize == 256;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return false;
        }
    }
}
