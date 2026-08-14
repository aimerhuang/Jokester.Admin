using jokester.admin.Common;

namespace jokester.admin.Tests;

public sealed class ErrorContractTests
{
    [Theory]
    [InlineData(ErrorCodes.InvalidCredentials, MachineErrorCodes.InvalidCredentials)]
    [InlineData(ErrorCodes.AccountDisabled, MachineErrorCodes.AccountDisabled)]
    [InlineData(ErrorCodes.InvalidRefreshToken, MachineErrorCodes.RefreshTokenExpired)]
    [InlineData(ErrorCodes.CaptchaRequired, MachineErrorCodes.CaptchaRequired)]
    [InlineData(ErrorCodes.LoginLocked, MachineErrorCodes.LoginLocked)]
    public void LegacyAuthenticationErrors_MapToStableMachineCodes(int legacyCode, string expected)
    {
        Assert.Equal(expected, MachineErrorCodes.FromLegacyCode(legacyCode));
    }
}
