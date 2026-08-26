using System.Text;
using jokester.admin.Application.Security;

namespace jokester.admin.Tests;

public sealed class RegistrationIdentityFactoryTests
{
    [Fact]
    public void Create_UsesTheEmailAccountAsTheDefaultNames()
    {
        var identity = RegistrationIdentityFactory.Create("email.account@example.test");

        Assert.Equal("email.account", identity.UserName);
        Assert.Equal("email.account", identity.NickName);
    }

    [Fact]
    public void Create_TruncatesUnicodeNamesToFiftyCodePoints()
    {
        var account = string.Concat(Enumerable.Repeat("\U0001F600", 51));

        var identity = RegistrationIdentityFactory.Create($"{account}@example.test");

        Assert.Equal(50, identity.UserName.EnumerateRunes().Count());
        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F600", 50)), identity.UserName);
        Assert.Equal(identity.UserName, identity.NickName);
    }

    [Fact]
    public void CreateDisambiguatedUserName_DiffersForMatchingAccountsAtDifferentDomains()
    {
        var first = RegistrationIdentityFactory.CreateDisambiguatedUserName("same@example.test");
        var second = RegistrationIdentityFactory.CreateDisambiguatedUserName("same@example.org");

        Assert.StartsWith("same_", first, StringComparison.Ordinal);
        Assert.StartsWith("same_", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.True(first.Length <= 50);
        Assert.True(second.Length <= 50);
    }
}
