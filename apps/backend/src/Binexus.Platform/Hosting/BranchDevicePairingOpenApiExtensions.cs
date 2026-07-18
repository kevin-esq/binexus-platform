using Binexus.Platform.Branching.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Binexus.Platform.Hosting;

/// <summary>
/// OpenAPI metadata for Branch pairing and desktop health surfaces (<c>branch-v1</c> document).
/// </summary>
internal static class BranchDevicePairingOpenApiExtensions
{
    internal static RouteHandlerBuilder WithBranchPairingOpenApi(this RouteHandlerBuilder builder) =>
        builder.WithGroupName(BranchDevicePairingEndpointExtensions.BranchDocumentGroup);

    internal static RouteHandlerBuilder WithCreateSessionOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Produces<CreatePairingSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

    internal static RouteHandlerBuilder WithGetRequestOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Produces<PairingRequestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

    internal static RouteHandlerBuilder WithApproveRequestOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Produces<ApprovePairingRequestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    internal static RouteHandlerBuilder WithRejectRequestOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Produces<RejectPairingRequestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    internal static RouteHandlerBuilder WithRevokeDeviceOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Produces<RevokeDeviceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

    internal static RouteHandlerBuilder WithListDevicesOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Produces<PairedDeviceResponse[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    internal static RouteHandlerBuilder WithListTerminalsOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Produces<BranchTerminalResponse[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    internal static RouteHandlerBuilder WithCreateExchangeChallengeOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Accepts<CreateExchangeChallengeRequest>("application/json")
            .Produces<CreateExchangeChallengeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

    internal static RouteHandlerBuilder WithExchangeOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Accepts<PairingExchangeRequest>("application/json")
            .Produces<PairingExchangeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

    internal static RouteHandlerBuilder WithStatusOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Accepts<PairingStatusRequest>("application/json")
            .Produces<PairingStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    internal static RouteHandlerBuilder WithReceiptReissueChallengeOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Accepts<CreateReceiptReissueChallengeRequest>("application/json")
            .Produces<CreateReceiptReissueChallengeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    internal static RouteHandlerBuilder WithReceiptReissueOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Accepts<ReissuePairingReceiptRequest>("application/json")
            .Produces<ReissuePairingReceiptResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    internal static RouteHandlerBuilder WithConfirmOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Accepts<PairingConfirmRequest>("application/json")
            .Produces<PairingConfirmResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    internal static RouteHandlerBuilder WithRuntimeHealthOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Produces<RuntimeHealthResponse>(StatusCodes.Status200OK);

    internal static RouteHandlerBuilder WithBranchHealthOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithBranchPairingOpenApi()
            .Produces<BranchHealthResponse>(StatusCodes.Status200OK);
}
