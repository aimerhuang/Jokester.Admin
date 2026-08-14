using System.Security.Cryptography;
using System.Text;
using jokester.admin.Infrastructure;
using jokester.admin.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace jokester.admin.Tests;

public sealed class AppleAppAccountTokenServiceTests
{
    [Fact]
    public void GetForUser_UsesRfc4122ByteOrderAndIsDeterministic()
    {
        const string bundleId = "cc.jokester.ai";
        const string key = "test-app-account-token-key-at-least-32-bytes";
        var service = new AppleAppAccountTokenService(Options.Create(new AppleAppStoreOptions
        {
            BundleId = bundleId,
            AppAccountTokenKey = key
        }));

        var actual = service.GetForUser(42);
        var bytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(key),
            Encoding.UTF8.GetBytes($"{bundleId}|42"))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        var expected = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";

        Assert.Equal(expected, actual);
        Assert.Equal(actual, service.GetForUser(42));
        Assert.NotEqual(actual, service.GetForUser(43));
        Assert.True(Guid.TryParseExact(actual, "D", out _));
        Assert.Equal('8', actual[14]);
        Assert.Contains(actual[19], "89ab");
    }

    [Fact]
    public void Validator_AllowsDisabledEmptyConfiguration()
    {
        var result = new AppleAppStoreOptionsValidator().Validate(null, new AppleAppStoreOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validator_RejectsIncompleteEnabledConfiguration()
    {
        var result = new AppleAppStoreOptionsValidator().Validate(null, new AppleAppStoreOptions
        {
            Enabled = true
        });

        Assert.False(result.Succeeded);
        var failures = Assert.IsAssignableFrom<IEnumerable<string>>(result.Failures);
        Assert.Contains(failures, failure => failure.Contains("BundleId", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("PrivateKeyPem", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsCompleteEnabledConfiguration()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var result = new AppleAppStoreOptionsValidator().Validate(null, new AppleAppStoreOptions
        {
            Enabled = true,
            BundleId = "cc.jokester.ai",
            IssuerId = Guid.NewGuid().ToString("D"),
            KeyId = "APPSTOREKEY",
            PrivateKeyPem = key.ExportPkcs8PrivateKeyPem(),
            AppAccountTokenKey = "test-app-account-token-key-at-least-32-bytes",
            ProductionBaseUrl = "https://api.storekit.itunes.apple.com",
            SandboxBaseUrl = "https://api.storekit-sandbox.itunes.apple.com",
            Environment = "Both"
        });

        Assert.True(result.Succeeded);
    }
}
