using System.Security.Claims;
using Binexus.Platform.Branching.DeviceAuth;
using Binexus.Platform.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Platform.Hosting;

public static class BranchDeviceAuthEndpointExtensions
{
    public static IEndpointRouteBuilder MapBranchDeviceAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var runtime = endpoints.ServiceProvider.GetService<IRuntimeDescriptor>();
        if (runtime?.Mode != RuntimeMode.Branch)
        {
            return endpoints;
        }

        var group = endpoints.MapGroup("/branch/device-auth")
            .WithTags("BranchDeviceAuth")
            .WithGroupName(BranchDevicePairingEndpointExtensions.BranchDocumentGroup);

        group.MapPost("/challenges", CreateChallengeAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-device-auth")
            .WithCreateDeviceAuthChallengeOpenApi();

        group.MapPost("/tokens", IssueTokenAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-device-auth")
            .WithIssueDeviceAuthTokenOpenApi();

        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization(DeviceAuthCryptoFormats.DeviceOnlyPolicy)
            .WithDeviceAuthMeOpenApi();

        return endpoints;
    }

    private static async Task<IResult> CreateChallengeAsync(
        CreateDeviceAuthChallengeRequest body,
        IBranchDeviceAuthService auth,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await auth.CreateChallengeAsync(body.DeviceId, cancellationToken);
            return Results.Ok(result);
        }
        catch (DeviceAuthException ex)
        {
            return DeviceAuthHttp.FromException(ex);
        }
    }

    private static async Task<IResult> IssueTokenAsync(
        IssueDeviceAuthTokenRequest body,
        IBranchDeviceAuthService auth,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await auth.IssueTokenAsync(
                body.ChallengeId,
                body.DeviceId,
                body.Signature,
                body.ProtocolVersion,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (DeviceAuthException ex)
        {
            return DeviceAuthHttp.FromException(ex);
        }
    }

    private static async Task<IResult> GetMeAsync(
        HttpContext http,
        IBranchDeviceAuthService auth,
        CancellationToken cancellationToken)
    {
        try
        {
            var deviceIdText = http.User.FindFirstValue("sub")
                ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (deviceIdText is null || !Guid.TryParse(deviceIdText, out var deviceId))
            {
                throw new DeviceAuthException(DeviceAuthErrorCodes.DeviceAuthRequired, "Device auth required.");
            }

            var result = await auth.GetMeAsync(deviceId, cancellationToken);
            return Results.Ok(result);
        }
        catch (DeviceAuthException ex)
        {
            return DeviceAuthHttp.FromException(ex);
        }
    }
}

internal static class DeviceAuthHttp
{
    public static IResult FromException(DeviceAuthException ex)
    {
        var status = ex.Code switch
        {
            DeviceAuthErrorCodes.DeviceChallengeReplayed => StatusCodes.Status409Conflict,
            DeviceAuthErrorCodes.DeviceStatusUnavailable => StatusCodes.Status503ServiceUnavailable,
            DeviceAuthErrorCodes.DeviceRevoked
                or DeviceAuthErrorCodes.DeviceNotActive
                or DeviceAuthErrorCodes.DeviceBranchMismatch
                or DeviceAuthErrorCodes.DeviceTerminalMissing
                or DeviceAuthErrorCodes.DeviceTerminalDisabled
                or DeviceAuthErrorCodes.DeviceBindingInvalid
                or DeviceAuthErrorCodes.UserBranchMismatch => StatusCodes.Status403Forbidden,
            DeviceAuthErrorCodes.DeviceChallengeExpired => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status401Unauthorized,
        };

        return Results.Problem(
            detail: ex.Message,
            statusCode: status,
            title: ex.Code,
            type: $"https://binexus.dev/errors/{ex.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
    }
}
