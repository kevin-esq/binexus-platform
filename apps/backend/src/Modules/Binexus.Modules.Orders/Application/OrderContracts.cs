using Binexus.Modules.Orders.Domain;
using Binexus.SharedKernel.Abstractions;
using Binexus.SharedKernel.Results;

namespace Binexus.Modules.Orders.Application;

public sealed record CreateOrderLineRequest(string ProductId, string ProductName, int Quantity, int UnitPriceCents);
public sealed record CreateOrderRequest(Guid? BranchId, string CustomerId, string Currency, string PaymentMethod, IReadOnlyList<CreateOrderLineRequest> Lines);
public sealed record OrderLineSummary(Guid Id, string ProductId, string ProductName, int Quantity, int UnitPriceCents, int LineTotalCents);
public sealed record OrderTransitionSummary(Guid Id, string? FromState, string ToState, string? Reason, DateTimeOffset OccurredAt, Guid? ByUserId);
public sealed record OrderSummary(Guid Id, Guid BranchId, string CustomerId, string State, string PaymentMethod, int TotalCents, string Currency, DateTimeOffset CreatedAt, int LineCount);
public sealed record OrderDetail(
    Guid Id, Guid BranchId, string CustomerId, string State, string PaymentMethod, int TotalCents, string Currency,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid CreatedByUserId, IReadOnlyList<OrderLineSummary> Lines,
    IReadOnlyList<OrderTransitionSummary> Transitions);
public sealed record ListOrdersQuery(int? Limit, string? Cursor);
public sealed record ListOrdersResult(IReadOnlyList<OrderSummary> Items, string? NextCursor);
public sealed record CreateOrderResult(Guid Id);
public sealed record OrderMutationResult(Guid Id, string State);

public sealed record CreateOrderCommand(Guid OrderId, CreateOrderRequest Request, string? OperationKey, string? CorrelationId) : ITransactionalCommand;
public sealed record ApproveOrderCommand(Guid OrderId, string? OperationKey, string? CorrelationId) : ITransactionalCommand;
public sealed record CancelOrderCommand(Guid OrderId, string? Reason, string? OperationKey, string? CorrelationId) : ITransactionalCommand;
public sealed record RequeueFailedDeliveryOrderCommand(Guid OrderId, string? Reason, string? OperationKey, string? CorrelationId) : ITransactionalCommand;
public sealed record MoveOrderToPickingCommand(Guid OrderId, Guid ActorId, string? Reason, string? CorrelationId) : ITransactionalCommand;
public sealed record MarkOrderReadyForDeliveryRouteCommand(Guid OrderId, Guid ActorId, string? Reason, string? CorrelationId) : ITransactionalCommand;
public sealed record MarkOrderOutForDeliveryCommand(Guid OrderId, Guid ActorId, string? Reason, string? CorrelationId) : ITransactionalCommand;
public sealed record MarkOrderDeliveredCommand(Guid OrderId, Guid ActorId, string? Reason, string? CorrelationId) : ITransactionalCommand;
public sealed record MarkOrderDeliveryAttemptFailedCommand(Guid OrderId, Guid ActorId, string? Reason, string? CorrelationId) : ITransactionalCommand;
public sealed record SettleOrderCommand(Guid OrderId, Guid ActorId, string? Reason, string? CorrelationId) : ITransactionalCommand;

public interface IOrdersQueryService
{
    Task<Result<ListOrdersResult>> ListAsync(ListOrdersQuery query, CancellationToken ct);
    Task<Result<OrderDetail>> GetAsync(Guid orderId, CancellationToken ct);
    Task<Result<OrderDetail?>> FindByOperationKeyAsync(string operationKey, CancellationToken ct);
}

public static class OrdersErrorMapping
{
    public static DomainError ToDomainError(OrdersDomainException ex) =>
        ex.Code switch
        {
            OrdersError.OrderNotFound => DomainError.NotFound(ex.Code, ex.Message),
            OrdersError.InvalidTransition or OrdersError.InsufficientStock or OrdersError.ConcurrencyConflict
                or OrdersError.IdempotencyKeyConflict or OrdersError.IdempotencyKeyReused =>
                DomainError.Conflict(ex.Code, ex.Message),
            "FORBIDDEN" => DomainError.Forbidden(ex.Code, ex.Message),
            _ => DomainError.Validation(ex.Code, ex.Message),
        };
}
