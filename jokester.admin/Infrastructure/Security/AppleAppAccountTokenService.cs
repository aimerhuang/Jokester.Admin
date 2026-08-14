using System.Security.Cryptography;
using System.Text;
using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using Microsoft.Extensions.Options;

namespace jokester.admin.Infrastructure.Security;

public sealed class AppleAppAccountTokenService(IOptions<AppleAppStoreOptions> options) : IAppleAppAccountTokenService
{
    private readonly AppleAppStoreOptions _options = options.Value;

    public string GetForUser(long userId)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(_options.AppAccountTokenKey))
        {
            throw new AppException(
                ErrorCodes.ServiceUnavailable,
                MachineErrorCodes.ServiceUnavailable,
                "Apple app account token configuration is unavailable.");
        }
        var key = Encoding.UTF8.GetBytes(_options.AppAccountTokenKey);
        var material = Encoding.UTF8.GetBytes($"{_options.BundleId}|{userId}");
        var bytes = HMACSHA256.HashData(key, material)[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes, bigEndian: true).ToString("D");
    }
}
