using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Application.Security;

public static class RegistrationUserNameValidator
{
    public const int MinLength = 6;
    public const int MaxLength = 20;

    public static string Validate(string? userName)
    {
        if (string.IsNullOrEmpty(userName) || userName.Length is < MinLength or > MaxLength)
        {
            throw InvalidUserName();
        }

        var hasLetter = false;
        var hasDigit = false;
        foreach (var character in userName)
        {
            if (char.IsAsciiLetter(character))
            {
                hasLetter = true;
            }
            else if (char.IsAsciiDigit(character))
            {
                hasDigit = true;
            }
            else
            {
                throw InvalidUserName();
            }
        }

        if (!hasLetter || !hasDigit)
        {
            throw InvalidUserName();
        }

        return userName;
    }

    private static AppException InvalidUserName() =>
        new(ErrorCodes.BadRequest, "账户名必须为 6-20 位字母和数字组合，且至少各含一个，不能包含空格或特殊字符");
}
