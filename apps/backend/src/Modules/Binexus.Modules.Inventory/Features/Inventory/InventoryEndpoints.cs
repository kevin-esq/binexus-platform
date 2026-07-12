using Binexus.Modules.Inventory.Application;
using Binexus.Modules.Inventory.Domain;
using Binexus.Platform.Tenancy;
using Binexus.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Binexus.Modules.Inventory.Features.Inventory;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/inventory/stock").WithTags("Inventory").RequireAuthorization();
        group.MapGet("", ListStockAsync).Produces<ListStockItemsResult>();
        group.MapPost("/adjust", AdjustAsync).Produces<AdjustStockResult>().ProducesProblem(400).ProducesProblem(409);
        group.MapPost("/transfers", CreateTransferAsync).Produces<StockTransferSummary>().ProducesProblem(400);
        group.MapGet("/transfers", ListTransfersAsync).Produces<ListStockTransfersResult>();
        group.MapPost("/transfers/{id:guid}/receive", ReceiveAsync).Produces<ReceiveStockTransferResult>().ProducesProblem(409);
        group.MapPost("/transfers/{id:guid}/cancel", CancelAsync).Produces<StockTransferSummary>().ProducesProblem(409);
        return endpoints;
    }

    private static async Task<IResult> ListStockAsync(Guid? branchId, string? productId, int? limit, string? cursor, IInventoryService service, CancellationToken ct) =>
        FromResult(await service.ListStockAsync(new(branchId, productId, limit, cursor), ct), Results.Ok);

    private static async Task<IResult> AdjustAsync(AdjustStockRequest request, HttpRequest httpRequest, ICurrentTenant tenant, IInventoryService service, CancellationToken ct)
    {
        var key = ResolveOperationKey(httpRequest, tenant, "adjust", request.OperationKey);
        if (key.IsFailure)
        {
            return ToProblem(key.Error!);
        }

        return FromResult(
            await service.AdjustAsync(request with { OperationKey = key.Value }, ct),
            Results.Ok);
    }

    private static async Task<IResult> CreateTransferAsync(CreateStockTransferRequest request, HttpRequest httpRequest, ICurrentTenant tenant, IInventoryService service, CancellationToken ct)
    {
        var key = ResolveOperationKey(httpRequest, tenant, "transfer-create", request.OperationKey);
        if (key.IsFailure)
        {
            return ToProblem(key.Error!);
        }

        return FromResult(
            await service.CreateTransferAsync(request with { OperationKey = key.Value }, ct),
            x => Results.Ok(new { transfer = x }));
    }

    private static async Task<IResult> ListTransfersAsync(string? status, int? limit, string? cursor, IInventoryService service, CancellationToken ct) =>
        FromResult(await service.ListTransfersAsync(new(status, limit, cursor), ct), Results.Ok);

    private static async Task<IResult> ReceiveAsync(Guid id, HttpRequest httpRequest, ICurrentTenant tenant, IInventoryService service, CancellationToken ct)
    {
        var key = ResolveOperationKey(httpRequest, tenant, "transfer-receive", null);
        if (key.IsFailure)
        {
            return ToProblem(key.Error!);
        }

        return FromResult(await service.ReceiveTransferAsync(id, ct), Results.Ok);
    }

    private static async Task<IResult> CancelAsync(Guid id, HttpRequest httpRequest, ICurrentTenant tenant, IInventoryService service, CancellationToken ct)
    {
        var key = ResolveOperationKey(httpRequest, tenant, "transfer-cancel", null);
        if (key.IsFailure)
        {
            return ToProblem(key.Error!);
        }

        return FromResult(await service.CancelTransferAsync(id, ct), x => Results.Ok(new { transfer = x }));
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

    private static Result<string?> ResolveOperationKey(HttpRequest request, ICurrentTenant currentTenant, string operation, string? fallback)
    {
        var key = request.Headers.TryGetValue("Idempotency-Key", out var header) ? header.ToString() : fallback;
        if (string.IsNullOrWhiteSpace(key))
        {
            return ResultFactory.Ok<string?>(null);
        }

        if (key.Length is < 1 or > 128 || key.Any(character => character is < '!' or > '~'))
        {
            return ResultFactory.Fail<string?>(DomainError.Validation(
                InventoryError.InvalidAdjustment,
                "Idempotency-Key must contain 1-128 printable ASCII characters."));
        }

        if (currentTenant.Current?.TenantId is not Guid tenantId)
        {
            return ResultFactory.Fail<string?>(DomainError.Forbidden("FORBIDDEN", "Tenant context is required."));
        }

        return ResultFactory.Ok<string?>($"{operation}:{tenantId}:{key}");
    }
}
