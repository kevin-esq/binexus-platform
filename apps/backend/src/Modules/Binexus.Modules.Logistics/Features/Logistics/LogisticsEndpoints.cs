using Binexus.Modules.Logistics.Application;
using Binexus.Modules.Logistics.Domain;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Ids;
using Binexus.Platform.Tenancy;
using Binexus.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Binexus.Modules.Logistics.Features.Logistics;

public static class LogisticsEndpoints
{
    public static IEndpointRouteBuilder MapLogisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/logistics").WithTags("Logistics").RequireAuthorization();
        group.MapGet("/delivery-route-candidates", ListCandidatesAsync).Produces<ListDeliveryRouteCandidatesResult>();
        group.MapGet("/delivery-routes", ListRoutesAsync).Produces<ListDeliveryRoutesResult>();
        group.MapPost("/delivery-routes", CreateRouteAsync).Produces<DeliveryRouteSummary>().ProducesProblem(409);
        group.MapPost("/delivery-routes/{id:guid}/assign-orders", AssignOrdersAsync).Produces<DeliveryRouteSummary>().ProducesProblem(409);
        group.MapPost("/delivery-routes/{id:guid}/dispatch", DispatchAsync).Produces<DeliveryRouteSummary>().ProducesProblem(409);
        group.MapGet("/delivery-routes/{id:guid}/stops", ListStopsAsync).Produces<ListDeliveryRouteStopsResult>().ProducesProblem(404);
        group.MapPost("/delivery-route-stops/{id:guid}/proof-uploads", ProofUploadAsync).Produces<DeliveryProofUploadResult>().ProducesProblem(409);
        group.MapPost("/delivery-route-stops/{id:guid}/confirm-delivery", ConfirmAsync).Produces<DeliveryRouteStopSummary>().ProducesProblem(409);
        group.MapPost("/delivery-route-stops/{id:guid}/report-failed-delivery", ReportFailedAsync).Produces<DeliveryRouteStopSummary>().ProducesProblem(409);
        group.MapPost("/delivery-routes/{id:guid}/liquidate", LiquidateAsync).Produces<DeliveryRouteSummary>().ProducesProblem(403).ProducesProblem(409);
        return endpoints;
    }

    private static async Task<IResult> ListCandidatesAsync(string? status, Guid? branchId, int? limit, string? cursor, ILogisticsQueryService queries, CancellationToken ct) =>
        FromResult(await queries.ListCandidatesAsync(new(status, branchId, limit, cursor), ct), Results.Ok);

    private static async Task<IResult> ListRoutesAsync(string? status, Guid? branchId, int? limit, string? cursor, ILogisticsQueryService queries, CancellationToken ct) =>
        FromResult(await queries.ListRoutesAsync(new(status, branchId, limit, cursor), ct), Results.Ok);

    private static async Task<IResult> ListStopsAsync(Guid id, ILogisticsQueryService queries, CancellationToken ct) =>
        FromResult(await queries.ListStopsAsync(id, ct), Results.Ok);

    private static async Task<IResult> CreateRouteAsync(CreateDeliveryRouteRequest body, HttpRequest request, IIdGenerator ids, ICommandDispatcher dispatcher, ILogisticsQueryService queries, CancellationToken ct)
    {
        var operationKey = IdempotencyKey(request);
        var id = ids.NewId();
        var dispatched = await dispatcher.DispatchAsync(new CreateDeliveryRouteCommand(id, body, operationKey, Correlation(request)), ct);
        return dispatched.IsFailure ? ToProblem(dispatched.Error!) : FromResult(await queries.GetRouteByCreationOperationKeyAsync(operationKey, ct), Results.Ok);
    }

    private static async Task<IResult> AssignOrdersAsync(Guid id, AssignOrdersRequest body, HttpRequest request, ICommandDispatcher dispatcher, ILogisticsQueryService queries, CancellationToken ct)
    {
        var dispatched = await dispatcher.DispatchAsync(new AssignOrdersToDeliveryRouteCommand(id, body, IdempotencyKey(request), Correlation(request)), ct);
        return dispatched.IsFailure ? ToProblem(dispatched.Error!) : FromResult(await queries.GetRouteAsync(id, ct), Results.Ok);
    }

    private static async Task<IResult> DispatchAsync(Guid id, DispatchDeliveryRouteRequest body, HttpRequest request, ICommandDispatcher dispatcher, ILogisticsQueryService queries, CancellationToken ct)
    {
        var dispatched = await dispatcher.DispatchAsync(new DispatchDeliveryRouteCommand(id, body, IdempotencyKey(request), Correlation(request)), ct);
        return dispatched.IsFailure ? ToProblem(dispatched.Error!) : FromResult(await queries.GetRouteAsync(id, ct), Results.Ok);
    }

    private static async Task<IResult> ProofUploadAsync(
        Guid id,
        CreateDeliveryProofUploadRequest body,
        HttpRequest request,
        ILogisticsProofUploadService uploads,
        CancellationToken ct)
    {
        // Server decides bucket, key, TTL, MIME, max size — client only sends kind, contentType, sizeBytes.
        var optionalKey = request.Headers.TryGetValue("Idempotency-Key", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString().Trim()
            : null;
        return FromResult(await uploads.CreateAsync(id, body, optionalKey, ct), Results.Ok);
    }

    private static async Task<IResult> ConfirmAsync(
        Guid id,
        ConfirmDeliveryRequest body,
        HttpRequest request,
        ICurrentTenant currentTenant,
        ILogisticsProofObjectVerifier proofVerifier,
        ICommandDispatcher dispatcher,
        ILogisticsQueryService queries,
        CancellationToken ct)
    {
        var tenantId = currentTenant.Current?.TenantId
            ?? throw new InvalidOperationException("Tenant context is required.");
        try
        {
            // HeadObject / ExistsAsync outside the ConfirmDelivery PG transaction.
            await proofVerifier.EnsureProofObjectsExistAsync(tenantId, id, body.Proof, ct);
        }
        catch (LogisticsDomainException ex)
        {
            return ToProblem(LogisticsErrorMapping.ToDomainError(ex));
        }

        var dispatched = await dispatcher.DispatchAsync(new ConfirmDeliveryCommand(id, body, IdempotencyKey(request), Correlation(request)), ct);
        return dispatched.IsFailure ? ToProblem(dispatched.Error!) : FromResult(await queries.GetStopAsync(id, ct), Results.Ok);
    }

    private static async Task<IResult> ReportFailedAsync(Guid id, ReportFailedDeliveryRequest body, HttpRequest request, ICommandDispatcher dispatcher, ILogisticsQueryService queries, CancellationToken ct)
    {
        var dispatched = await dispatcher.DispatchAsync(new ReportFailedDeliveryCommand(id, body, IdempotencyKey(request), Correlation(request)), ct);
        return dispatched.IsFailure ? ToProblem(dispatched.Error!) : FromResult(await queries.GetStopAsync(id, ct), Results.Ok);
    }

    private static async Task<IResult> LiquidateAsync(Guid id, LiquidateDeliveryRouteRequest body, HttpRequest request, ICommandDispatcher dispatcher, ILogisticsQueryService queries, CancellationToken ct)
    {
        var dispatched = await dispatcher.DispatchAsync(new LiquidateDeliveryRouteCommand(id, body, IdempotencyKey(request), Correlation(request)), ct);
        return dispatched.IsFailure ? ToProblem(dispatched.Error!) : FromResult(await queries.GetRouteAsync(id, ct), Results.Ok);
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
