using System.Security.Cryptography;
using System.Text;

namespace jokester.admin.Application.Security;

public static class PointRedeemCodeSecurity
{
    public static string Normalize(string value) => value.Trim();

    public static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(value))));

    public static string Generate()
    {
        var payload = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        return "JAI-" + string.Join('-', Enumerable.Range(0, 6).Select(index => payload.Substring(index * 4, 4)));
    }

    public static string Mask(string value)
    {
        var normalized = Normalize(value);
        if (normalized.Length <= 12)
        {
            return new string('*', normalized.Length);
        }

        return $"{normalized[..8]}-****-****-{normalized[^4..]}";
    }
}
