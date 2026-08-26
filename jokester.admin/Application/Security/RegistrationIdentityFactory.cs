using System.Security.Cryptography;
using System.Text;

namespace jokester.admin.Application.Security;

public sealed record RegistrationIdentity(string UserName, string NickName);

public static class RegistrationIdentityFactory
{
    private const int MaxNameLength = 50;
    private const int HashLength = 12;

    public static RegistrationIdentity Create(string normalizedEmail)
    {
        var account = GetEmailAccount(normalizedEmail);
        var defaultName = Truncate(account, MaxNameLength);
        return new RegistrationIdentity(defaultName, defaultName);
    }

    public static string CreateDisambiguatedUserName(string normalizedEmail)
    {
        var account = GetEmailAccount(normalizedEmail);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)))
            .ToLowerInvariant()[..HashLength];
        var suffix = $"_{hash}";
        return Truncate(account, MaxNameLength - suffix.Length) + suffix;
    }

    private static string GetEmailAccount(string normalizedEmail)
    {
        var separator = normalizedEmail.LastIndexOf('@');
        return separator > 0 ? normalizedEmail[..separator] : "user";
    }

    private static string Truncate(string value, int maxLength) =>
        value.EnumerateRunes().Take(maxLength).Aggregate(new StringBuilder(), (builder, rune) => builder.Append(rune)).ToString();
}
