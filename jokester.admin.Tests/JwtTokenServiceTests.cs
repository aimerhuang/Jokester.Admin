using System.IdentityModel.Tokens.Jwt;
using jokester.admin.Infrastructure;
using jokester.admin.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace jokester.admin.Tests;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void CreateAccessToken_UsesExpectedAlgorithmAndSessionClaim()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "https://issuer.example",
            Audience = "jokester-app",
            SecretKey = "this-is-a-test-secret-with-at-least-32-bytes",
            AccessTokenExpiresMinutes = 15,
            RefreshTokenExpiresDays = 7
        }));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            service.CreateAccessToken(42, "tester", false, "session-claim-value"));

        Assert.Equal(SecurityAlgorithms.HmacSha256, token.Header.Alg);
        Assert.Equal("session-claim-value", token.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Sid).Value);
        Assert.False(string.IsNullOrWhiteSpace(token.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Jti).Value));
    }
}
