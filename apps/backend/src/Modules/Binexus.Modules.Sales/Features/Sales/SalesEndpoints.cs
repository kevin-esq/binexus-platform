using Binexus.Modules.Sales.Application;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Ids;
using Binexus.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Binexus.Modules.Sales.Features.Sales;

public static class SalesEndpoints
{
    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/sales/sessions").WithTags("Sales").RequireAuthorization();
        group.MapPost("/open", OpenAsync).Produces<OpenSalesSessionResult>().ProducesProblem(403).ProducesProblem(409);
        group.MapGet("/current", GetCurrentAsync).Produces<GetCurrentSalesSessionResult>().ProducesProblem(403);
        group.MapGet("/{id:guid}", GetByIdAsync).Produces<SalesSessionSummary>().ProducesProblem(404).ProducesProblem(403);
        group.MapPost("/{id:guid}/sales", CreateSaleAsync).Produces<CreateSaleResult>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(409);
        group.MapPost("/{id:guid}/close", CloseAsync).Produces<CloseSalesSessionResult>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(409);
        return endpoints;
    }

    private static async Task<IResult> OpenAsync(
        OpenSalesSessionRequest body,
        HttpRequest request,
        IIdGenerator ids,
        ICommandDispatcher dispatcher,
        ISalesQueryService queries,
        CancellationToken ct)
    {
        var operationKey = IdempotencyKey(request);
        var dispatched = await dispatcher.DispatchAsync(
            new OpenSalesSessionCommand(ids.NewId(), body, operationKey, Correlation(request)),
            ct);
        if (dispatched.IsFailure)
        {
            return ToProblem(dispatched.Error!);
        }

        return FromResult(await queries.GetByOpenOperationKeyAsync(operationKey, ct), session => Results.Ok(new OpenSalesSessionResult(session)));
    }

    private static async Task<IResult> GetCurrentAsync(
        string terminalId,
        Guid? branchId,
        ISalesQueryService queries,
        CancellationToken ct) =>
        FromResult(await queries.GetCurrentAsync(terminalId, branchId, ct), Results.Ok);

    private static async Task<IResult> GetByIdAsync(Guid id, ISalesQueryService queries, CancellationToken ct) =>
        FromResult(await queries.GetByIdAsync(id, ct), Results.Ok);

    private static async Task<IResult> CreateSaleAsync(
        Guid id,
        CreateSaleRequest body,
        HttpRequest request,
        IIdGenerator ids,
        ICommandDispatcher dispatcher,
        ISalesQueryService queries,
        CancellationToken ct)
    {
        var operationKey = IdempotencyKey(request);
        var saleId = ids.NewId();
        var dispatched = await dispatcher.DispatchAsync(
            new CreateSaleCommand(id, saleId, body, operationKey, Correlation(request)),
            ct);
        if (dispatched.IsFailure)
        {
            return ToProblem(dispatched.Error!);
        }

        return FromResult(await queries.GetSaleByOperationKeyAsync(operationKey, ct), ticket => Results.Ok(new CreateSaleResult(ticket)));
    }

    private static async Task<IResult> CloseAsync(
        Guid id,
        CloseSalesSessionRequest body,
        HttpRequest request,
        ICommandDispatcher dispatcher,
        ISalesQueryService queries,
        CancellationToken ct)
    {
        var dispatched = await dispatcher.DispatchAsync(
            new CloseSalesSessionCommand(id, body, IdempotencyKey(request), Correlation(request)),
            ct);
        if (dispatched.IsFailure)
        {
            return ToProblem(dispatched.Error!);
        }

        return FromResult(await queries.GetByIdAsync(id, ct), session => Results.Ok(new CloseSalesSessionResult(session)));
    }

    private static IResult FromResult<T>(Result<T> result, Func<T, IResult> success) =>
        result.IsFailure ? ToProblem(result.Error!) : success(result.Value!);

    private static IResult ToProblem(DomainError error)
    {
        var status = error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden or ErrorKind.Unauthorized => StatusCodes.Status403Forbidden,
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
