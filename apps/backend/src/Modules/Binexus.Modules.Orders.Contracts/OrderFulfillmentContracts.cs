namespace Binexus.Modules.Orders.Contracts;

public enum OrderFulfillmentOutcome
{
    Success,
    AlreadyApplied,
    NoLongerApplicable,
    NotFound,
    ConcurrencyConflict,
}

public sealed record OrderFulfillmentResult(OrderFulfillmentOutcome Outcome, string? Code = null, string? Message = null);

public sealed record OrderFulfillmentRequest(
    Guid TenantId,
    Guid OrderId,
    Guid? ActorId,
    Guid? CorrelationId,
    Guid CausationId,
    string? Reason = null,
    string? Source = null);

public sealed record OrderFulfillmentBatchRequest(
    Guid TenantId,
    IReadOnlyList<Guid> OrderIds,
    Guid? ActorId,
    Guid? CorrelationId,
    Guid CausationId,
    string? Reason = null,
    string? Source = null);

public sealed record CashCollectionFact(
    Guid OrderId,
    string PaymentMethod,
    int TotalCents,
    string Currency,
    string State);

public sealed record CashCollectionFactsResult(
    IReadOnlyList<CashCollectionFact> Facts,
    IReadOnlyList<Guid> MissingOrderIds);

public sealed record SettleCodOrdersRequest(
    Guid TenantId,
    IReadOnlyList<Guid> OrderIds,
    Guid? ActorId,
    Guid? CorrelationId,
    Guid CausationId,
    string? Reason = null,
    string? Source = null);

public interface IOrderFulfillmentApi
{
    Task<OrderFulfillmentResult> MoveToPickingAsync(OrderFulfillmentRequest request, CancellationToken ct);

    Task<OrderFulfillmentResult> MarkReadyForDeliveryRouteAsync(OrderFulfillmentRequest request, CancellationToken ct);

    Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(OrderFulfillmentRequest request, CancellationToken ct);

    Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(OrderFulfillmentBatchRequest request, CancellationToken ct);

    Task<OrderFulfillmentResult> MarkDeliveredAsync(OrderFulfillmentRequest request, CancellationToken ct);

    Task<OrderFulfillmentResult> MarkDeliveryAttemptFailedAsync(OrderFulfillmentRequest request, CancellationToken ct);

    Task<OrderFulfillmentResult> SettleCodOrdersAsync(SettleCodOrdersRequest request, CancellationToken ct);

    Task<CashCollectionFactsResult> GetCashCollectionFactsAsync(Guid tenantId, IReadOnlyList<Guid> orderIds, CancellationToken ct);
}
