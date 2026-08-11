using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Application.Security;

public static class AiImagePromptValidator
{
    public const int MaxLength = 4000;

    public static string Validate(string prompt, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            if (allowEmpty)
            {
                return string.Empty;
            }

            throw new AppException(ErrorCodes.BadRequest, "Prompt is required");
        }

        var trimmed = prompt.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw new AppException(ErrorCodes.BadRequest, "Prompt is too long");
        }

        return trimmed;
    }
}
