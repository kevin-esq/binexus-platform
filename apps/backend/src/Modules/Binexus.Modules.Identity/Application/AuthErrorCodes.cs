namespace Binexus.Modules.Identity.Application;

public static class AuthErrorCodes
{
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";
    public const string RefreshTokenReused = "REFRESH_TOKEN_REUSED";
    public const string TokenExpired = "TOKEN_EXPIRED";

    /// <summary>Public only for authenticated surfaces such as GET /auth/me after disable.</summary>
    public const string AccountUnavailable = "ACCOUNT_UNAVAILABLE";

    public const string Forbidden = "FORBIDDEN";
}

/// <summary>Internal login failure taxonomy — never returned to clients.</summary>
public static class LoginFailedReason
{
    public const string TenantNotFound = "TenantNotFound";
    public const string UserNotFound = "UserNotFound";
    public const string InvalidPassword = "InvalidPassword";
    public const string UserDisabled = "UserDisabled";
    public const string UnknownRole = "UnknownRole";
}

public sealed class AuthException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
