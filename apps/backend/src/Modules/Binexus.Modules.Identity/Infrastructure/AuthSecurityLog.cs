using Microsoft.Extensions.Logging;

namespace Binexus.Modules.Identity.Infrastructure;

internal static partial class AuthSecurityLog
{
    [LoggerMessage(1001, LogLevel.Information, "Authentication succeeded for user {UserId} in tenant {TenantId}")]
    public static partial void LoginSucceeded(ILogger logger, Guid userId, Guid tenantId);

    [LoggerMessage(1002, LogLevel.Warning, "Refresh token reuse detected for family {FamilyId}")]
    public static partial void RefreshReuseDetected(ILogger logger, Guid familyId);

    [LoggerMessage(1003, LogLevel.Information, "Refresh token revoked during logout for user {UserId}")]
    public static partial void LogoutSucceeded(ILogger logger, Guid userId);

    [LoggerMessage(1004, LogLevel.Information, "Refresh succeeded for user {UserId} in tenant {TenantId}")]
    public static partial void RefreshSucceeded(ILogger logger, Guid userId, Guid tenantId);

    [LoggerMessage(1005, LogLevel.Warning, "Authentication failed ({Reason}) user {UserId} tenant {TenantId}")]
    public static partial void LoginFailed(
        ILogger logger,
        string reason,
        Guid? userId = null,
        Guid? tenantId = null);
}
