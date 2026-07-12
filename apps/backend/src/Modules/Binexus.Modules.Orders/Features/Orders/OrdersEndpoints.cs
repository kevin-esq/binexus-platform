using Binexus.Modules.Orders.Application;
using Binexus.Modules.Orders.Domain;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Ids;
using Binexus.Platform.Tenancy;
using Binexus.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Binexus.Modules.Orders.Features.Orders;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orders").WithTags("Orders").RequireAuthorization();
        group.MapGet("", ListAsync).Produces<ListOrdersResult>();
        group.MapGet("/{id:guid}", GetAsync).Produces<OrderDetail>().ProducesProblem(404);
        group.MapPost("", CreateAsync).Produces<CreateOrderResult>(StatusCodes.Status201Created).Produces<CreateOrderResult>().ProducesProblem(400).ProducesProblem(409);
        group.MapPost("/{id:guid}/approve", ApproveAsync).Produces<OrderMutationResult>().ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/{id:guid}/cancel", CancelAsync).Produces<OrderMutationResult>().ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/{id:guid}/requeue-for-delivery", RequeueAsync).Produces<OrderMutationResult>().ProducesProblem(404).ProducesProblem(409);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(int? limit, string? cursor, IOrdersQueryService queries, CancellationToken ct) =>
        FromResult(await queries.ListAsync(new(limit, cursor), ct), Results.Ok);

    private static async Task<IResult> GetAsync(Guid id, IOrdersQueryService queries, CancellationToken ct) =>
        FromResult(await queries.GetAsync(id, ct), Results.Ok);

    private static async Task<IResult> CreateAsync(CreateOrderRequest request, HttpRequest http, ICurrentTenant tenant, ICommandDispatcher dispatcher, IOrdersQueryService queries, IIdGenerator ids, CancellationToken ct)
    {
        var operationKey = ResolveOperationKey(http, tenant, "order-create");
        if (operationKey.IsFailure)
        {
            return ToProblem(operationKey.Error!);
        }

        if (operationKey.Value is not null)
        {
            var existing = await queries.FindByOperationKeyAsync(operationKey.Value, ct);
            if (existing.IsFailure)
            {
                return ToProblem(existing.Error!);
            }

            if (existing.Value is { } found)
            {
                var replay = await dispatcher.DispatchAsync(
                    new CreateOrderCommand(found.Id, request, operationKey.Value, Correlation(http)),
                    ct);
                return replay.IsFailure ? ToProblem(replay.Error!) : Results.Ok(new CreateOrderResult(found.Id));
            }
        }

        var id = ids.NewId();
        var result = await dispatcher.DispatchAsync(
            new CreateOrderCommand(id, request, operationKey.Value, Correlation(http)),
            ct);
        return result.IsFailure ? ToProblem(result.Error!) : Results.Created($"/orders/{id}", new CreateOrderResult(id));
    }

    private static async Task<IResult> ApproveAsync(Guid id, HttpRequest http, ICurrentTenant tenant, ICommandDispatcher dispatcher, IOrdersQueryService queries, CancellationToken ct)
    {
        var operationKey = ResolveOperationKey(http, tenant, "order-approve");
        if (operationKey.IsFailure)
        {
            return ToProblem(operationKey.Error!);
        }

        var dispatched = await dispatcher.DispatchAsync(
            new ApproveOrderCommand(id, operationKey.Value, Correlation(http)),
            ct);
        if (dispatched.IsFailure)
        {
            return ToProblem(dispatched.Error!);
        }

        return FromResult(await queries.GetAsync(id, ct), detail => Results.Ok(new OrderMutationResult(detail.Id, detail.State)));
    }

    private static async Task<IResult> CancelAsync(Guid id, CancelOrderRequest? request, HttpRequest http, ICurrentTenant tenant, ICommandDispatcher dispatcher, IOrdersQueryService queries, CancellationToken ct)
    {
        var operationKey = ResolveOperationKey(http, tenant, "order-cancel");
        if (operationKey.IsFailure)
        {
            return ToProblem(operationKey.Error!);
        }

        var dispatched = await dispatcher.DispatchAsync(
            new CancelOrderCommand(id, request?.Reason, operationKey.Value, Correlation(http)),
            ct);
        if (dispatched.IsFailure)
        {
            return ToProblem(dispatched.Error!);
        }

        return FromResult(await queries.GetAsync(id, ct), detail => Results.Ok(new OrderMutationResult(detail.Id, detail.State)));
    }

    private static async Task<IResult> RequeueAsync(Guid id, RequeueOrderRequest? request, HttpRequest http, ICurrentTenant tenant, ICommandDispatcher dispatcher, IOrdersQueryService queries, CancellationToken ct)
    {
        var operationKey = ResolveOperationKey(http, tenant, "order-requeue");
        if (operationKey.IsFailure)
        {
            return ToProblem(operationKey.Error!);
        }

        var dispatched = await dispatcher.DispatchAsync(
            new RequeueFailedDeliveryOrderCommand(id, request?.Reason, operationKey.Value, Correlation(http)),
            ct);
        if (dispatched.IsFailure)
        {
            return ToProblem(dispatched.Error!);
        }

        return FromResult(await queries.GetAsync(id, ct), detail => Results.Ok(new OrderMutationResult(detail.Id, detail.State)));
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
            error.Message,
            statusCode: status,
            title: error.Code,
            type: $"https://binexus.dev/errors/{error.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    private static Result<string?> ResolveOperationKey(HttpRequest request, ICurrentTenant tenant, string operation)
    {
        if (!request.Headers.TryGetValue("Idempotency-Key", out var value) || string.IsNullOrWhiteSpace(value))
        {
            return ResultFactory.Ok<string?>(null);
        }

        var key = value.ToString();
        if (key.Length is < 1 or > 128 || key.Any(c => c is < '!' or > '~'))
        {
            return ResultFactory.Fail<string?>(DomainError.Validation(
                OrdersError.InvalidOrder,
                "Idempotency-Key must contain 1-128 printable ASCII characters."));
        }

        if (tenant.Current?.TenantId is not Guid tenantId)
        {
            return ResultFactory.Fail<string?>(DomainError.Forbidden("FORBIDDEN", "Tenant context is required."));
        }

        return ResultFactory.Ok<string?>($"{operation}:{tenantId}:{key}");
    }

    private static string? Correlation(HttpRequest request) =>
        request.Headers.TryGetValue("X-Correlation-Id", out var value) ? value.ToString() : null;
}

public sealed record CancelOrderRequest(string? Reason);
public sealed record RequeueOrderRequest(string? Reason);
