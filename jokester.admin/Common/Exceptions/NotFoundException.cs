namespace jokester.admin.Common.Exceptions;

public sealed class NotFoundException(string message) : AppException(ErrorCodes.NotFound, message);
