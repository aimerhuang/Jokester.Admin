using jokester.admin.Application.Security;

namespace jokester.admin.Tests;

public sealed class PointRedeemCodeSecurityTests
{
    [Fact]
    public void Hash_IsStable_AndCaseSensitive()
    {
        var first = PointRedeemCodeSecurity.Hash("  JAI-AbCd-1234  ");
        var repeated = PointRedeemCodeSecurity.Hash("JAI-AbCd-1234");
        var differentCase = PointRedeemCodeSecurity.Hash("JAI-ABCD-1234");

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, differentCase);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void Generate_ProducesUniqueMaskedCodes()
    {
        var codes = Enumerable.Range(0, 100)
            .Select(_ => PointRedeemCodeSecurity.Generate())
            .ToArray();

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code =>
        {
            Assert.StartsWith("JAI-", code, StringComparison.Ordinal);
            Assert.DoesNotContain(code, PointRedeemCodeSecurity.Mask(code), StringComparison.Ordinal);
        });
    }
}
