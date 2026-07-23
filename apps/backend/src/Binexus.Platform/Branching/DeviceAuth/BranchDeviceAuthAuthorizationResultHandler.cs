using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Binexus.Platform.Branching.DeviceAuth;

/// <summary>
/// Maps Branch Dev+User authorization failures to stable Problem Details codes
/// (<c>DEVICE_AUTH_REQUIRED</c> / <c>USER_AUTH_REQUIRED</c>) instead of opaque 403.
/// </summary>
public sealed class BranchDeviceAuthAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var requiresDeviceAndUser = policy.Requirements.OfType<BranchDeviceAndUserRequirement>().Any();
        if (!requiresDeviceAndUser)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        if (context.Items.TryGetValue(DeviceAuthCryptoFormats.FailureCodeItemKey, out var failureRaw)
            && failureRaw is string failureCode
            && failureCode == DeviceAuthErrorCodes.DeviceStatusUnavailable)
        {
            await WriteAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                failureCode,
                "Device status unavailable.");
            return;
        }

        var hasUser = context.User.Identities.Any(i =>
            i.IsAuthenticated
            && !string.Equals(i.AuthenticationType, DeviceAuthCryptoFormats.AuthenticationScheme, StringComparison.Ordinal));
        var hasDevice = context.User.Identities.Any(i =>
            i.IsAuthenticated
            && string.Equals(i.AuthenticationType, DeviceAuthCryptoFormats.AuthenticationScheme, StringComparison.Ordinal));

        if (!hasDevice)
        {
            await WriteAsync(context, StatusCodes.Status401Unauthorized, DeviceAuthErrorCodes.DeviceAuthRequired, "Device auth required.");
            return;
        }

        if (!hasUser)
        {
            await WriteAsync(context, StatusCodes.Status401Unauthorized, DeviceAuthErrorCodes.UserAuthRequired, "User auth required.");
            return;
        }

        if (!BranchDeviceAndUserClaims.AreCoherent(context.User))
        {
            await WriteAsync(
                context,
                StatusCodes.Status403Forbidden,
                DeviceAuthErrorCodes.UserBranchMismatch,
                "User tenant or branch does not match the device.");
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new
        {
            type = $"https://binexus.dev/errors/{code}",
            title = code,
            status,
            detail,
            code,
        });
    }
}
