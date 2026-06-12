using jokester.admin.Common;

namespace jokester.admin.Common.Exceptions;

public class AppException : Exception
{
    public AppException(int code, string message) : base(message)
    {
        Code = code;
    }

    public int Code { get; }
}
