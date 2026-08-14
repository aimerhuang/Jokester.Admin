using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Tests;

public sealed class RegistrationUserNameValidatorTests
{
    [Theory]
    [InlineData("abc123")]
    [InlineData("Account2026")]
    [InlineData("abcdefghij1234567890")]
    public void Validate_AcceptsSixToTwentyAsciiLettersAndDigits(string userName)
    {
        Assert.Equal(userName, RegistrationUserNameValidator.Validate(userName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc12")]
    [InlineData("abcdefghij12345678901")]
    [InlineData("abcdef")]
    [InlineData("123456")]
    [InlineData("abc_123")]
    [InlineData("abc-123")]
    [InlineData("abc.123")]
    [InlineData("abc@123")]
    [InlineData("abc 123")]
    [InlineData(" abc123")]
    [InlineData("abc123 ")]
    [InlineData("用户1234")]
    [InlineData("abc123😀")]
    [InlineData("abc１２３")]
    public void Validate_RejectsSimpleOrUnsupportedUserNames(string? userName)
    {
        var exception = Assert.Throws<AppException>(() => RegistrationUserNameValidator.Validate(userName));

        Assert.Equal(ErrorCodes.BadRequest, exception.Code);
        Assert.Equal(MachineErrorCodes.ValidationError, exception.MachineCode);
    }
}
