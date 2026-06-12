namespace jokester.admin.Common.Exceptions;

public sealed class ConflictException(string message) : AppException(ErrorCodes.BadRequest, message);
