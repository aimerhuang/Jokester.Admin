namespace jokester.admin.Common;

public static class ErrorCodes
{
    public const int Success = 200;
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int TooManyRequests = 429;
    public const int ServerError = 500;
    public const int InvalidCredentials = 1001;
    public const int AccountDisabled = 1002;
    public const int InvalidRefreshToken = 1003;
    public const int CaptchaRequired = 1004;
    public const int LoginLocked = 1005;
}
