using System.Security.Claims;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Pairing;
using Binexus.Platform.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Platform.Hosting;

public static class BranchDevicePairingEndpointExtensions
{
    /// <summary>OpenAPI document group. Keeps pairing out of the Cloud <c>binexus-v1</c> document.</summary>
    public const string BranchDocumentGroup = "branch-v1";

    public static IEndpointRouteBuilder MapBranchDevicePairingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var runtime = endpoints.ServiceProvider.GetService<IRuntimeDescriptor>();
        if (runtime?.Mode != RuntimeMode.Branch)
        {
            return endpoints;
        }

        MapAdminEndpoints(endpoints);
        MapMachineEndpoints(endpoints);
        return endpoints;
    }

    private static void MapAdminEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/branch/pairing").WithTags("BranchPairingAdmin").WithGroupName(BranchDocumentGroup);

        group.MapPost("/sessions", CreateSessionAsync)
            .RequireAuthorization()
            .RequireRateLimiting("branch-pairing-admin")
            .WithCreateSessionOpenApi();

        group.MapGet("/requests/{pairingRequestId:guid}", GetRequestAsync)
            .RequireAuthorization()
            .RequireRateLimiting("branch-pairing-admin")
            .WithGetRequestOpenApi();

        group.MapPost("/requests/{pairingRequestId:guid}/approve", ApproveRequestAsync)
            .RequireAuthorization()
            .RequireRateLimiting("branch-pairing-admin")
            .WithApproveRequestOpenApi();

        group.MapPost("/requests/{pairingRequestId:guid}/reject", RejectRequestAsync)
            .RequireAuthorization()
            .RequireRateLimiting("branch-pairing-admin")
            .WithRejectRequestOpenApi();

        var devices = endpoints.MapGroup("/branch/devices").WithTags("BranchPairingAdmin").WithGroupName(BranchDocumentGroup);
        devices.MapGet("/", ListDevicesAsync)
            .RequireAuthorization()
            .RequireRateLimiting("branch-pairing-admin")
            .WithListDevicesOpenApi();
        devices.MapPost("/{deviceId:guid}/revoke", RevokeDeviceAsync)
            .RequireAuthorization()
            .RequireRateLimiting("branch-pairing-admin")
            .WithRevokeDeviceOpenApi();
        devices.MapPost("/{deviceId:guid}/terminals/rebind", RebindTerminalAsync)
            .RequireAuthorization()
            .RequireRateLimiting("branch-pairing-admin")
            .WithRebindTerminalOpenApi();

        endpoints.MapGet("/branch/terminals", ListTerminalsAsync)
            .WithTags("BranchPairingAdmin")
            .WithGroupName(BranchDocumentGroup)
            .RequireAuthorization()
            .RequireRateLimiting("branch-pairing-admin")
            .WithListTerminalsOpenApi();
        endpoints.MapPost("/branch/terminals/{terminalId:guid}/disable", DisableTerminalAsync)
            .WithTags("BranchPairingAdmin")
            .WithGroupName(BranchDocumentGroup)
            .RequireAuthorization()
            .RequireRateLimiting("branch-pairing-admin")
            .WithDisableTerminalOpenApi();
    }

    private static void MapMachineEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/branch/pairing").WithTags("BranchPairingMachine").WithGroupName(BranchDocumentGroup);

        group.MapPost("/challenges", CreateExchangeChallengeAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-pairing-machine")
            .WithCreateExchangeChallengeOpenApi();

        group.MapPost("/exchange", ExchangeAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-pairing-machine")
            .WithExchangeOpenApi();

        group.MapPost("/requests/{pairingRequestId:guid}/status", StatusAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-pairing-machine")
            .WithStatusOpenApi();

        group.MapPost("/requests/{pairingRequestId:guid}/receipt/challenges", CreateReceiptReissueChallengeAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-pairing-machine")
            .WithReceiptReissueChallengeOpenApi();

        group.MapPost("/requests/{pairingRequestId:guid}/receipt/reissue", ReissueReceiptAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-pairing-machine")
            .WithReceiptReissueOpenApi();

        group.MapPost("/confirm", ConfirmAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-pairing-machine")
            .WithConfirmOpenApi();
    }

    private static async Task<IResult> CreateSessionAsync(
        ClaimsPrincipal user,
        IBranchDeviceAdminService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminContext(user, out var context))
        {
            return PairingError(new DevicePairingException(DevicePairingErrorCodes.Forbidden, "Missing identity claims."));
        }

        try
        {
            var result = await service.CreateSessionAsync(
                context.TenantId, context.BranchId, context.UserId, context.Role, cancellationToken);
            return Results.Ok(new CreatePairingSessionResponse(
                result.PairingSessionId, result.PairingCode, result.ExpiresAtUtc));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> GetRequestAsync(
        Guid pairingRequestId,
        ClaimsPrincipal user,
        IBranchDeviceAdminService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminContext(user, out var context))
        {
            return PairingError(new DevicePairingException(DevicePairingErrorCodes.Forbidden, "Missing identity claims."));
        }

        try
        {
            var request = await service.GetRequestAsync(
                context.TenantId, context.BranchId, pairingRequestId, cancellationToken);
            return Results.Ok(ToResponse(request));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> ApproveRequestAsync(
        Guid pairingRequestId,
        ClaimsPrincipal user,
        IBranchDeviceAdminService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminContext(user, out var context))
        {
            return PairingError(new DevicePairingException(DevicePairingErrorCodes.Forbidden, "Missing identity claims."));
        }

        try
        {
            var result = await service.ApproveRequestAsync(
                context.TenantId, context.BranchId, context.UserId, context.Role, pairingRequestId, cancellationToken);
            return Results.Ok(new ApprovePairingRequestResponse(
                result.PairingRequestId, result.DeviceId, result.TerminalId, result.ConfirmationChallengeId, result.Status));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> RejectRequestAsync(
        Guid pairingRequestId,
        ClaimsPrincipal user,
        IBranchDeviceAdminService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminContext(user, out var context))
        {
            return PairingError(new DevicePairingException(DevicePairingErrorCodes.Forbidden, "Missing identity claims."));
        }

        try
        {
            var result = await service.RejectRequestAsync(
                context.TenantId, context.BranchId, context.UserId, context.Role, pairingRequestId, cancellationToken);
            return Results.Ok(new RejectPairingRequestResponse(result.PairingRequestId, result.Status));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> RevokeDeviceAsync(
        Guid deviceId,
        ClaimsPrincipal user,
        IBranchDeviceAdminService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminContext(user, out var context))
        {
            return PairingError(new DevicePairingException(DevicePairingErrorCodes.Forbidden, "Missing identity claims."));
        }

        try
        {
            var result = await service.RevokeDeviceAsync(
                context.TenantId, context.BranchId, context.UserId, context.Role, deviceId, cancellationToken);
            return Results.Ok(new RevokeDeviceResponse(result.DeviceId, result.TerminalId, result.DeviceStatus));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> DisableTerminalAsync(
        Guid terminalId,
        ClaimsPrincipal user,
        IBranchDeviceAdminService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminContext(user, out var context))
        {
            return PairingError(new DevicePairingException(DevicePairingErrorCodes.Forbidden, "Missing identity claims."));
        }

        try
        {
            var result = await service.DisableTerminalAsync(
                context.TenantId, context.BranchId, context.UserId, context.Role, terminalId, cancellationToken);
            return Results.Ok(new DisableTerminalResponse(result.TerminalId, result.DeviceId, result.TerminalStatus));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> RebindTerminalAsync(
        Guid deviceId,
        RebindTerminalRequest body,
        ClaimsPrincipal user,
        IBranchDeviceAdminService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminContext(user, out var context))
        {
            return PairingError(new DevicePairingException(DevicePairingErrorCodes.Forbidden, "Missing identity claims."));
        }

        try
        {
            var result = await service.RebindTerminalAsync(
                context.TenantId,
                context.BranchId,
                context.UserId,
                context.Role,
                deviceId,
                body.TerminalName,
                cancellationToken);
            return Results.Ok(new RebindTerminalResponse(
                result.DeviceId,
                result.PreviousTerminalId,
                result.NewTerminalId,
                result.NewTerminalName));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> ListDevicesAsync(
        ClaimsPrincipal user,
        IBranchDeviceAdminService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminContext(user, out var context))
        {
            return PairingError(new DevicePairingException(DevicePairingErrorCodes.Forbidden, "Missing identity claims."));
        }

        try
        {
            var devices = await service.ListDevicesAsync(context.TenantId, context.BranchId, cancellationToken);
            return Results.Ok(devices.Select(d => new PairedDeviceResponse(
                d.DeviceId, d.PublicKeyFingerprint, d.DeviceFingerprintShort, d.Status, d.CreatedAtUtc, d.PairedAtUtc, d.RevokedAtUtc)));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> ListTerminalsAsync(
        ClaimsPrincipal user,
        IBranchDeviceAdminService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminContext(user, out var context))
        {
            return PairingError(new DevicePairingException(DevicePairingErrorCodes.Forbidden, "Missing identity claims."));
        }

        try
        {
            var terminals = await service.ListTerminalsAsync(context.TenantId, context.BranchId, cancellationToken);
            return Results.Ok(terminals.Select(t => new BranchTerminalResponse(
                t.TerminalId, t.DeviceId, t.Name, t.Status, t.CreatedAtUtc, t.ActivatedAtUtc)));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> CreateExchangeChallengeAsync(
        CreateExchangeChallengeRequest request,
        IBranchDevicePairingService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreateExchangeChallengeAsync(
                request.PairingSessionId, request.PairingCode, request.DeviceId, request.PublicKey, request.CredentialHash, cancellationToken);
            return Results.Ok(new CreateExchangeChallengeResponse(
                result.ChallengeId, result.BranchInstanceId, result.Nonce, result.ExpiresAtUtc));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> ExchangeAsync(
        PairingExchangeRequest request,
        IBranchDevicePairingService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ExchangeAsync(
                request.PairingSessionId,
                request.PairingCode,
                request.DeviceId,
                request.PublicKey,
                request.ChallengeId,
                request.Signature,
                request.CredentialHash,
                request.TerminalName,
                cancellationToken);
            return Results.Ok(new PairingExchangeResponse(
                result.PairingRequestId, result.DeviceFingerprintShort, result.Status, result.PairingStatusToken, result.ExpiresAtUtc));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> StatusAsync(
        Guid pairingRequestId,
        PairingStatusRequest request,
        IBranchDevicePairingService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetStatusAsync(pairingRequestId, request.PairingStatusToken, cancellationToken);
            return Results.Ok(new PairingStatusResponse(
                result.PairingRequestId,
                result.Status,
                result.BranchInstanceId,
                result.TerminalId,
                result.ConfirmationChallengeId,
                result.ConfirmationNonce,
                result.ConfirmationExpiresAtUtc,
                result.PairingReceipt));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> CreateReceiptReissueChallengeAsync(
        Guid pairingRequestId,
        CreateReceiptReissueChallengeRequest request,
        IBranchDevicePairingService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreateReceiptReissueChallengeAsync(
                pairingRequestId, request.PairingStatusToken, cancellationToken);
            return Results.Ok(new CreateReceiptReissueChallengeResponse(
                result.ChallengeId, result.BranchInstanceId, result.Nonce, result.ExpiresAtUtc));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> ReissueReceiptAsync(
        Guid pairingRequestId,
        ReissuePairingReceiptRequest request,
        IBranchDevicePairingService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ReissueReceiptAsync(
                pairingRequestId,
                request.PairingStatusToken,
                request.ReissueChallengeId,
                request.Signature,
                cancellationToken);
            return Results.Ok(new ReissuePairingReceiptResponse(
                result.PairingRequestId,
                result.BranchInstanceId,
                result.TerminalId,
                result.PairingReceipt,
                result.ConfirmationChallengeId,
                result.ConfirmationNonce,
                result.ConfirmationExpiresAtUtc));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static async Task<IResult> ConfirmAsync(
        PairingConfirmRequest request,
        IBranchDevicePairingService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ConfirmAsync(
                request.PairingRequestId,
                request.ConfirmationChallengeId,
                request.Signature,
                request.PairingReceipt,
                request.PairingStatusToken,
                cancellationToken);
            return Results.Ok(new PairingConfirmResponse(
                result.PairingRequestId, result.DeviceId, result.TerminalId, result.Status, result.AlreadyActive));
        }
        catch (DevicePairingException exception)
        {
            return PairingError(exception);
        }
    }

    private static PairingRequestResponse ToResponse(PairingRequestView request) =>
        new(
            request.PairingRequestId,
            request.DeviceId,
            request.DeviceFingerprintShort,
            request.RequestedTerminalName,
            request.Status,
            request.RequestedAtUtc,
            request.ExpiresAtUtc,
            request.TerminalId,
            request.ApprovedAtUtc,
            request.RejectedAtUtc,
            request.CompletedAtUtc);

    private static bool TryGetAdminContext(ClaimsPrincipal user, out AdminContext context)
    {
        context = default;
        if (!Guid.TryParse(user.FindFirstValue("tenantId"), out var tenantId)
            || !Guid.TryParse(user.FindFirstValue("sub"), out var userId)
            || !Guid.TryParse(user.FindFirstValue("branchId"), out var branchId))
        {
            return false;
        }

        var role = user.FindFirstValue("role") ?? string.Empty;
        context = new AdminContext(tenantId, branchId, userId, role);
        return true;
    }

    private static IResult PairingError(DevicePairingException exception)
    {
        var status = exception.Code switch
        {
            DevicePairingErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            DevicePairingErrorCodes.PairingRequestNotFound => StatusCodes.Status404NotFound,
            DevicePairingErrorCodes.DeviceNotFound => StatusCodes.Status404NotFound,
            DevicePairingErrorCodes.PairingLocked => StatusCodes.Status429TooManyRequests,
            DevicePairingErrorCodes.BranchNotActive => StatusCodes.Status409Conflict,
            DevicePairingErrorCodes.PairingConflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Problem(
            detail: exception.Message,
            statusCode: status,
            title: exception.Code,
            type: $"https://binexus.dev/errors/{exception.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
    }

    private readonly record struct AdminContext(Guid TenantId, Guid BranchId, Guid UserId, string Role);
}
