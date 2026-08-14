using jokester.admin.Common;

namespace jokester.admin.Common.Exceptions;

public class AppException : Exception
{
    public AppException(int code, string message)
        : this(code, MachineErrorCodes.FromLegacyCode(code), message)
    {
    }

    public AppException(int code, string machineCode, string message, object? details = null) : base(message)
    {
        Code = code;
        MachineCode = machineCode;
        Details = details;
    }

    public int Code { get; }

    public string MachineCode { get; }

    public object? Details { get; }
}
