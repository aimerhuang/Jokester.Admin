using System.Security.Cryptography;
using jokester.admin.Application.Abstractions;

namespace jokester.admin.Application.Security;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public (string Hash, string Salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public bool Verify(string password, string passwordHash, string? salt)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrWhiteSpace(salt))
        {
            return false;
        }

        if (!TryDecodeBase64(salt, SaltSize, out var saltBytes)
            || !TryDecodeBase64(passwordHash, HashSize, out var expectedHash))
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static bool TryDecodeBase64(string value, int expectedByteCount, out byte[] bytes)
    {
        bytes = new byte[expectedByteCount];
        return Convert.TryFromBase64String(value, bytes, out var bytesWritten)
            && bytesWritten == expectedByteCount;
    }
}
