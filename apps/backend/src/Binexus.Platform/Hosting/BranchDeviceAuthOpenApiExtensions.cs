using Binexus.Platform.Branching.DeviceAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Binexus.Platform.Hosting;

/// <summary>OpenAPI metadata for Branch device-auth surfaces (<c>branch-v1</c>).</summary>
internal static class BranchDeviceAuthOpenApiExtensions
{
    internal static RouteHandlerBuilder WithDeviceAuthGroup(this RouteHandlerBuilder builder) =>
        builder.WithGroupName(BranchDevicePairingEndpointExtensions.BranchDocumentGroup);

    internal static RouteHandlerBuilder WithCreateDeviceAuthChallengeOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithDeviceAuthGroup()
            .Accepts<CreateDeviceAuthChallengeRequest>("application/json")
            .Produces<DeviceAuthChallengeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    internal static RouteHandlerBuilder WithIssueDeviceAuthTokenOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithDeviceAuthGroup()
            .Accepts<IssueDeviceAuthTokenRequest>("application/json")
            .Produces<DeviceAuthTokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    internal static RouteHandlerBuilder WithDeviceAuthMeOpenApi(this RouteHandlerBuilder builder) =>
        builder
            .WithDeviceAuthGroup()
            .Produces<DeviceAuthMeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
