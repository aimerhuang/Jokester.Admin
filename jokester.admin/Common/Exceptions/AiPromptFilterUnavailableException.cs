namespace jokester.admin.Common.Exceptions;

public sealed class AiPromptFilterUnavailableException()
    : AppException(ErrorCodes.ServiceUnavailable, "Prompt filter is temporarily unavailable");
