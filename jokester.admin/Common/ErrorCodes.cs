namespace jokester.admin.Common;

public static class ErrorCodes
{
    public const int Success = 0;
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int Conflict = 409;
    public const int PreconditionFailed = 412;
    public const int UnprocessableEntity = 422;
    public const int TooManyRequests = 429;
    public const int ServerError = 500;
    public const int ServiceUnavailable = 503;
    public const int InvalidCredentials = 1001;
    public const int AccountDisabled = 1002;
    public const int InvalidRefreshToken = 1003;
    public const int CaptchaRequired = 1004;
    public const int LoginLocked = 1005;
    public const int AiPromptRejected = 2001;
}

public static class MachineErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountDisabled = "ACCOUNT_DISABLED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string AccessTokenExpired = "ACCESS_TOKEN_EXPIRED";
    public const string RefreshTokenExpired = "REFRESH_TOKEN_EXPIRED";
    public const string CaptchaRequired = "CAPTCHA_REQUIRED";
    public const string LoginLocked = "LOGIN_LOCKED";
    public const string SessionRevoked = "SESSION_REVOKED";
    public const string ResourceForbidden = "RESOURCE_FORBIDDEN";
    public const string ResourceNotFound = "RESOURCE_NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string AppleTransactionOwnedByOtherUser = "APPLE_TRANSACTION_OWNED_BY_OTHER_USER";
    public const string AiConsentRequired = "AI_CONSENT_REQUIRED";
    public const string PromptBlocked = "PROMPT_BLOCKED";
    public const string RateLimited = "RATE_LIMITED";
    public const string ReauthenticationRequired = "REAUTH_REQUIRED";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    public const string ServerError = "SERVER_ERROR";

    public static string FromLegacyCode(int code) => code switch
    {
        ErrorCodes.BadRequest => ValidationError,
        ErrorCodes.Unauthorized => Unauthorized,
        ErrorCodes.InvalidCredentials => InvalidCredentials,
        ErrorCodes.AccountDisabled => AccountDisabled,
        ErrorCodes.InvalidRefreshToken => RefreshTokenExpired,
        ErrorCodes.CaptchaRequired => CaptchaRequired,
        ErrorCodes.LoginLocked => LoginLocked,
        ErrorCodes.Forbidden => ResourceForbidden,
        ErrorCodes.NotFound => ResourceNotFound,
        ErrorCodes.Conflict => Conflict,
        ErrorCodes.PreconditionFailed => AiConsentRequired,
        ErrorCodes.UnprocessableEntity or ErrorCodes.AiPromptRejected => PromptBlocked,
        ErrorCodes.TooManyRequests => RateLimited,
        ErrorCodes.ServiceUnavailable => ServiceUnavailable,
        _ => ServerError
    };
}
