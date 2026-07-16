using System.Security.Claims;
using Binexus.Platform.Branching.Activation;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Platform.Hosting;

public static class CloudBranchActivationEndpointExtensions
{
    public static IEndpointRouteBuilder MapCloudBranchActivationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var runtime = endpoints.ServiceProvider.GetService<IRuntimeDescriptor>();
        if (runtime?.Mode != RuntimeMode.Cloud)
        {
            return endpoints;
        }

        var group = endpoints.MapGroup("/cloud/branch-activations").WithTags("BranchActivation");

        group.MapPost("/", GenerateAsync)
            .RequireAuthorization()
            .RequireRateLimiting("branch-activation-generate")
            .WithName("GenerateBranchActivation")
            .Produces<GenerateBranchActivationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/challenges", CreateChallengeAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-activation-machine")
            .ExcludeFromDescription();

        group.MapPost("/exchange", ExchangeAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-activation-machine")
            .ExcludeFromDescription();

        group.MapPost("/{activationId:guid}/resume", ResumeAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-activation-machine")
            .ExcludeFromDescription();

        group.MapPost("/confirm", ConfirmAsync)
            .AllowAnonymous()
            .RequireRateLimiting("branch-activation-machine")
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> GenerateAsync(
        GenerateBranchActivationRequest request,
        ClaimsPrincipal user,
        ICloudBranchActivationService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetGuid(user, "tenantId", out var tenantId)
            || !TryGetGuid(user, "sub", out var userId))
        {
            return ActivationError(new BranchActivationException(
                BranchActivationErrorCodes.Forbidden,
                "Required identity claims are missing."));
        }

        var role = user.FindFirstValue("role");
        if (!string.Equals(role, "ADMIN", StringComparison.Ordinal)
            && !string.Equals(role, "SUPER_ADMIN", StringComparison.Ordinal))
        {
            return ActivationError(new BranchActivationException(
                BranchActivationErrorCodes.Forbidden,
                "ADMIN or SUPER_ADMIN role is required."));
        }

        try
        {
            var result = await service.GenerateAsync(tenantId, userId, request.BranchId, cancellationToken);
            return Results.Ok(new GenerateBranchActivationResponse(
                result.ActivationId,
                result.ActivationCode,
                result.ExpiresAtUtc));
        }
        catch (BranchActivationException exception)
        {
            return ActivationError(exception);
        }
    }

    private static async Task<IResult> CreateChallengeAsync(
        CreateChallengeRequest request,
        ICloudBranchActivationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreateChallengeAsync(
                request.BranchInstanceId,
                request.PublicKey,
                request.InstallationTokenHash,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (BranchActivationException exception)
        {
            return ActivationError(exception);
        }
    }

    private static async Task<IResult> ExchangeAsync(
        ExchangeRequest request,
        ICloudBranchActivationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ExchangeAsync(
                request.Code,
                request.BranchInstanceId,
                request.PublicKey,
                request.ChallengeId,
                request.Signature,
                request.InstallationTokenHash,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (BranchActivationException exception)
        {
            return ActivationError(exception);
        }
    }

    private static async Task<IResult> ConfirmAsync(
        ConfirmRequest request,
        HttpContext httpContext,
        ICloudBranchActivationService service,
        CancellationToken cancellationToken)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Branch ", StringComparison.Ordinal)
            || authorization.Length <= "Branch ".Length)
        {
            return ActivationError(Invalid());
        }

        var token = authorization["Branch ".Length..].Trim();
        try
        {
            var result = await service.ConfirmAsync(
                request.ActivationId,
                request.Receipt,
                token,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (BranchActivationException exception)
        {
            return ActivationError(exception);
        }
    }

    private static async Task<IResult> ResumeAsync(
        Guid activationId,
        ResumeRequest request,
        ICloudBranchActivationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ResumeAsync(
                activationId,
                request.BranchInstanceId,
                request.PublicKey,
                request.ChallengeId,
                request.Signature,
                request.InstallationTokenHash,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (BranchActivationException exception)
        {
            return ActivationError(exception);
        }
    }

    private static IResult ActivationError(BranchActivationException exception)
    {
        var status = exception.Code switch
        {
            BranchActivationErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            BranchActivationErrorCodes.BranchNotFound => StatusCodes.Status404NotFound,
            BranchActivationErrorCodes.BranchAlreadyActive
                or BranchActivationErrorCodes.ActivationInProgress => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Problem(
            detail: exception.Message,
            statusCode: status,
            title: exception.Code,
            type: $"https://binexus.dev/errors/{exception.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = exception.Code,
            });
    }

    private static bool TryGetGuid(ClaimsPrincipal principal, string claimType, out Guid value) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out value);

    private static BranchActivationException Invalid() =>
        new(BranchActivationErrorCodes.ActivationInvalid, "Activation request is invalid.");

    private sealed record GenerateBranchActivationRequest(Guid BranchId);

    private sealed record GenerateBranchActivationResponse(
        Guid ActivationId,
        string ActivationCode,
        DateTimeOffset ExpiresAtUtc);

    private sealed record CreateChallengeRequest(
        Guid BranchInstanceId,
        string PublicKey,
        string InstallationTokenHash);

    private sealed record ExchangeRequest(
        string Code,
        Guid BranchInstanceId,
        string PublicKey,
        Guid ChallengeId,
        string Signature,
        string InstallationTokenHash);

    private sealed record ConfirmRequest(Guid ActivationId, string Receipt);

    private sealed record ResumeRequest(
        Guid BranchInstanceId,
        string PublicKey,
        Guid ChallengeId,
        string Signature,
        string InstallationTokenHash);
}
