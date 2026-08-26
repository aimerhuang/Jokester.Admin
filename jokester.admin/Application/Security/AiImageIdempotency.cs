using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Application.Security;

public static class AiImageIdempotency
{
    private const int MinKeyLength = 8;
    private const int MaxKeyLength = 128;

    public static AiImageRequestIdentity Create(string? idempotencyKey, object canonicalRequest)
    {
        var keyHash = HashKey(idempotencyKey);
        var canonicalJson = JsonSerializer.Serialize(canonicalRequest);
        return new AiImageRequestIdentity(keyHash, Hash(canonicalJson));
    }

    public static string HashKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new AppException(ErrorCodes.BadRequest, "Idempotency key is required");
        }

        var normalizedKey = idempotencyKey.Trim();
        if (normalizedKey.Length is < MinKeyLength or > MaxKeyLength
            || normalizedKey.Any(char.IsControl))
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                $"Idempotency key must contain {MinKeyLength}-{MaxKeyLength} non-control characters");
        }

        return Hash(normalizedKey);
    }

    public static string DeriveTaskKeyHash(string rootKeyHash, int taskIndex)
    {
        if (taskIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskIndex));
        }

        return taskIndex == 0 ? rootKeyHash : Hash($"{rootKeyHash}:{taskIndex}");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record AiImageRequestIdentity(string KeyHash, string RequestFingerprint);
