using System.Security.Claims;
using System.Text.Encodings.Web;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Runtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.Branching.DeviceAuth;

public sealed class DeviceAccessTokenAuthenticationOptions : AuthenticationSchemeOptions
{
}

public sealed class DeviceAccessTokenAuthenticationHandler(
    IOptionsMonitor<DeviceAccessTokenAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDeviceAccessTokenValidator tokenValidator,
    IDeviceStatusResolver statusResolver,
    ICurrentDevice currentDevice,
    ICurrentTerminal currentTerminal,
    TimeProvider timeProvider)
    : AuthenticationHandler<DeviceAccessTokenAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(DeviceAuthCryptoFormats.DeviceAuthorizationHeader, out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var header = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            Context.Items[DeviceAuthCryptoFormats.FailureCodeItemKey] = DeviceAuthErrorCodes.DeviceAuthRequired;
            return AuthenticateResult.Fail(DeviceAuthErrorCodes.DeviceAuthRequired);
        }

        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            Context.Items[DeviceAuthCryptoFormats.FailureCodeItemKey] = DeviceAuthErrorCodes.DeviceAuthRequired;
            return AuthenticateResult.Fail(DeviceAuthErrorCodes.DeviceAuthRequired);
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var principal = tokenValidator.Validate(token, now);
            var deviceIdText = principal.FindFirstValue("sub")
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var stamp = principal.FindFirstValue("device_security_stamp");
            var instanceText = principal.FindFirstValue("branch_instance_id");
            var terminalClaim = principal.FindFirstValue("terminal_id");
            if (deviceIdText is null
                || stamp is null
                || instanceText is null
                || !Guid.TryParse(deviceIdText, out var deviceId)
                || !Guid.TryParse(instanceText, out var branchInstanceId))
            {
                Context.Items[DeviceAuthCryptoFormats.FailureCodeItemKey] = DeviceAuthErrorCodes.DeviceTokenInvalid;
                return AuthenticateResult.Fail(DeviceAuthErrorCodes.DeviceTokenInvalid);
            }

            var snapshot = await statusResolver.ResolveAsync(branchInstanceId, deviceId, Context.RequestAborted);
            if (snapshot.Status == BranchDevice.RevokedStatus)
            {
                Context.Items[DeviceAuthCryptoFormats.FailureCodeItemKey] = DeviceAuthErrorCodes.DeviceRevoked;
                return AuthenticateResult.Fail(DeviceAuthErrorCodes.DeviceRevoked);
            }

            if (snapshot.Status != BranchDevice.ActiveStatus)
            {
                Context.Items[DeviceAuthCryptoFormats.FailureCodeItemKey] = DeviceAuthErrorCodes.DeviceNotActive;
                return AuthenticateResult.Fail(DeviceAuthErrorCodes.DeviceNotActive);
            }

            if (!string.Equals(snapshot.SecurityStamp, stamp, StringComparison.Ordinal))
            {
                Context.Items[DeviceAuthCryptoFormats.FailureCodeItemKey] = DeviceAuthErrorCodes.DeviceTokenInvalid;
                return AuthenticateResult.Fail(DeviceAuthErrorCodes.DeviceTokenInvalid);
            }

            if (terminalClaim is not null
                && Guid.TryParse(terminalClaim, out var claimedTerminal)
                && claimedTerminal != snapshot.TerminalId)
            {
                Context.Items[DeviceAuthCryptoFormats.FailureCodeItemKey] = DeviceAuthErrorCodes.DeviceBindingInvalid;
                return AuthenticateResult.Fail(DeviceAuthErrorCodes.DeviceBindingInvalid);
            }

            currentDevice.SetContext(deviceId, snapshot.SecurityStamp);
            currentTerminal.SetContext(snapshot.TerminalId);

            var identity = new ClaimsIdentity(principal.Claims, DeviceAuthCryptoFormats.AuthenticationScheme);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
        catch (DeviceAuthException ex) when (ex.Code == DeviceAuthErrorCodes.DeviceStatusUnavailable)
        {
            Context.Items[DeviceAuthCryptoFormats.FailureCodeItemKey] = ex.Code;
            Context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return AuthenticateResult.Fail(ex.Code);
        }
        catch (DeviceAuthException ex)
        {
            Context.Items[DeviceAuthCryptoFormats.FailureCodeItemKey] = ex.Code;
            return AuthenticateResult.Fail(ex.Code);
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var code = Context.Items.TryGetValue(DeviceAuthCryptoFormats.FailureCodeItemKey, out var raw) && raw is string s
            ? s
            : DeviceAuthErrorCodes.DeviceAuthRequired;
        var status = code switch
        {
            DeviceAuthErrorCodes.DeviceStatusUnavailable => StatusCodes.Status503ServiceUnavailable,
            DeviceAuthErrorCodes.DeviceRevoked
                or DeviceAuthErrorCodes.DeviceNotActive
                or DeviceAuthErrorCodes.DeviceBranchMismatch
                or DeviceAuthErrorCodes.DeviceTerminalMissing
                or DeviceAuthErrorCodes.DeviceTerminalDisabled
                or DeviceAuthErrorCodes.DeviceBindingInvalid => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status401Unauthorized,
        };

        if (!Response.HasStarted)
        {
            Response.StatusCode = status;
            await Response.WriteAsJsonAsync(new
            {
                type = $"https://binexus.dev/errors/{code}",
                title = code,
                status,
                detail = code,
                code,
            });
        }
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        await HandleChallengeAsync(properties);
}

public sealed class BranchDeviceAndUserRequirement : IAuthorizationRequirement;

public sealed class BranchDeviceAndUserHandler : AuthorizationHandler<BranchDeviceAndUserRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BranchDeviceAndUserRequirement requirement)
    {
        var hasUser = context.User.Identities.Any(i =>
            i.IsAuthenticated
            && !string.Equals(i.AuthenticationType, DeviceAuthCryptoFormats.AuthenticationScheme, StringComparison.Ordinal));
        var hasDevice = context.User.Identities.Any(i =>
            i.IsAuthenticated
            && string.Equals(i.AuthenticationType, DeviceAuthCryptoFormats.AuthenticationScheme, StringComparison.Ordinal));

        if (hasUser && hasDevice && BranchDeviceAndUserClaims.AreCoherent(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal static class BranchDeviceAndUserClaims
{
    public static bool AreCoherent(System.Security.Claims.ClaimsPrincipal principal)
    {
        var user = principal.Identities.SingleOrDefault(identity =>
            identity.IsAuthenticated
            && !string.Equals(identity.AuthenticationType, DeviceAuthCryptoFormats.AuthenticationScheme, StringComparison.Ordinal));
        var device = principal.Identities.SingleOrDefault(identity =>
            identity.IsAuthenticated
            && string.Equals(identity.AuthenticationType, DeviceAuthCryptoFormats.AuthenticationScheme, StringComparison.Ordinal));

        return user is not null
            && device is not null
            && Guid.TryParse(user.FindFirst("tenantId")?.Value, out var userTenantId)
            && Guid.TryParse(user.FindFirst("branchId")?.Value, out var userBranchId)
            && Guid.TryParse(device.FindFirst("tenant_id")?.Value, out var deviceTenantId)
            && Guid.TryParse(device.FindFirst("branch_id")?.Value, out var deviceBranchId)
            && userTenantId == deviceTenantId
            && userBranchId == deviceBranchId;
    }
}

public sealed class BranchDeviceOnlyRequirement : IAuthorizationRequirement;

public sealed class BranchDeviceOnlyHandler : AuthorizationHandler<BranchDeviceOnlyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BranchDeviceOnlyRequirement requirement)
    {
        if (context.User.Identities.Any(i =>
                i.IsAuthenticated
                && string.Equals(i.AuthenticationType, DeviceAuthCryptoFormats.AuthenticationScheme, StringComparison.Ordinal)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class BranchOperationalAuthorizationExtensions
{
    public static RouteGroupBuilder RequireOperationalAuthorization(
        this RouteGroupBuilder group,
        IEndpointRouteBuilder endpoints)
    {
        var runtime = endpoints.ServiceProvider.GetService<IRuntimeDescriptor>();
        if (runtime?.Mode == RuntimeMode.Branch)
        {
            return group.RequireAuthorization(DeviceAuthCryptoFormats.DeviceAndUserPolicy);
        }

        return group.RequireAuthorization();
    }
}
