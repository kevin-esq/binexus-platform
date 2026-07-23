using Binexus.Modules.Warehouse.Application;
using Binexus.Platform.Branching.DeviceAuth;
using Binexus.Platform.Dispatching;
using Binexus.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Binexus.Modules.Warehouse.Features.Warehouse;

public static class WarehouseEndpoints
{
    public static IEndpointRouteBuilder MapWarehouseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/warehouse/picking-tasks").WithTags("Warehouse")
            .RequireOperationalAuthorization(endpoints);
        group.MapGet("", ListAsync).Produces<ListPickingTasksResult>();
        group.MapPost("/{id:guid}/complete", CompleteAsync).Produces<PickingTaskSummary>().ProducesProblem(404).ProducesProblem(409);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? status,
        Guid? branchId,
        int? limit,
        string? cursor,
        IWarehouseQueryService queries,
        CancellationToken ct) =>
        FromResult(await queries.ListAsync(new(status, branchId, limit, cursor), ct), Results.Ok);

    private static async Task<IResult> CompleteAsync(
        Guid id,
        HttpRequest request,
        ICommandDispatcher dispatcher,
        IWarehouseQueryService queries,
        CancellationToken ct)
    {
        var dispatched = await dispatcher.DispatchAsync(
            new CompletePickingTaskCommand(id, IdempotencyKey(request), Correlation(request)),
            ct);
        if (dispatched.IsFailure)
        {
            return ToProblem(dispatched.Error!);
        }

        return FromResult(await queries.GetAsync(id, ct), Results.Ok);
    }

    private static IResult FromResult<T>(Result<T> result, Func<T, IResult> success) =>
        result.IsFailure ? ToProblem(result.Error!) : success(result.Value!);

    private static IResult ToProblem(DomainError error)
    {
        var status = error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden or ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Problem(
            detail: error.Message,
            statusCode: status,
            title: error.Code,
            type: $"https://binexus.dev/errors/{error.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    private static string? Correlation(HttpRequest request) =>
        request.Headers.TryGetValue("X-Correlation-Id", out var value) ? value.ToString() : null;

    private static string IdempotencyKey(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Idempotency-Key", out var value) || string.IsNullOrWhiteSpace(value))
        {
            return Guid.NewGuid().ToString("N");
        }

        return value.ToString().Trim();
    }
}
