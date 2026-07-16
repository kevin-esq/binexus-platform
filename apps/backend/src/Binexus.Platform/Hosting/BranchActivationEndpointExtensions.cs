using Binexus.Platform.Branching.Activation;
using Binexus.Platform.Branching.Application;
using Binexus.Platform.Branching.Credentials;
using Binexus.Platform.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Platform.Hosting;

public static class BranchActivationEndpointExtensions
{
    public static IEndpointRouteBuilder MapBranchActivationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var runtime = endpoints.ServiceProvider.GetService<IRuntimeDescriptor>();
        if (runtime?.Mode != RuntimeMode.Branch)
        {
            return endpoints;
        }

        var group = endpoints.MapGroup("/branch/activation").WithTags("BranchActivation");

        group.MapPost("/", ActivateAsync)
            .ExcludeFromDescription();

        group.MapPost("/finalize", FinalizeAsync)
            .ExcludeFromDescription();

        group.MapGet("/status", StatusAsync)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> ActivateAsync(
        ActivateRequest request,
        IBranchActivationOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.ActivateAsync(request.Code, cancellationToken);
            return Results.Ok(new { stage = BranchActivationStage.Completed.ToString() });
        }
        catch (BranchActivationException exception)
        {
            return ActivationError(exception);
        }
    }

    private static async Task<IResult> FinalizeAsync(
        IBranchActivationOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.FinalizeAsync(cancellationToken);
            return Results.Ok(new { stage = BranchActivationStage.Completed.ToString() });
        }
        catch (BranchActivationException exception)
        {
            return ActivationError(exception);
        }
    }

    private static async Task<IResult> StatusAsync(
        IBranchActivationOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        var stage = await orchestrator.GetStatusAsync(cancellationToken);
        return Results.Ok(new { stage = stage.ToString() });
    }

    private static IResult ActivationError(BranchActivationException exception)
    {
        var status = exception.Code switch
        {
            BranchActivationErrorCodes.BranchAlreadyActive => StatusCodes.Status409Conflict,
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

    private sealed record ActivateRequest(string Code);
}
