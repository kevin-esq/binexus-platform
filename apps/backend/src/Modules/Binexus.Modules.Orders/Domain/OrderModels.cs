using Binexus.SharedKernel.Abstractions;

namespace Binexus.Modules.Orders.Domain;

public enum OrderState
{
    Draft,
    Approved,
    Picking,
    ReadyForDeliveryRoute,
    OutForDelivery,
    DeliveryAttemptFailed,
    Delivered,
    Settled,
    Cancelled,
}

public sealed class Order : ITenantScoped
{
    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderTransition> _transitions = [];

    private Order() { }

    public Order(
        Guid id,
        Guid tenantId,
        Guid branchId,
        string customerId,
        string currency,
        string paymentMethod,
        Guid createdByUserId,
        Guid initialTransitionId,
        IEnumerable<OrderLine> lines,
        DateTimeOffset now,
        string? operationKey = null,
        string? correlationId = null)
    {
        if (id == Guid.Empty) throw new OrdersDomainException(OrdersError.InvalidOrder, "Order id is required.");
        if (initialTransitionId == Guid.Empty) throw new OrdersDomainException(OrdersError.InvalidOrder, "Transition id is required.");
        if (branchId == Guid.Empty) throw new OrdersDomainException(OrdersError.InvalidOrder, "Branch is required.");
        if (createdByUserId == Guid.Empty) throw new OrdersDomainException(OrdersError.InvalidOrder, "Creator is required.");
        if (string.IsNullOrWhiteSpace(customerId) || customerId.Length > 256) throw new OrdersDomainException(OrdersError.InvalidOrder, "customerId is invalid.");
        if (currency is null || currency.Length != 3 || currency.Any(c => !char.IsAsciiLetter(c))) throw new OrdersDomainException(OrdersError.InvalidOrder, "currency must be a three-letter code.");
        if (string.IsNullOrWhiteSpace(paymentMethod) || paymentMethod.Length > 32) throw new OrdersDomainException(OrdersError.InvalidOrder, "paymentMethod is invalid.");

        var materializedLines = lines.ToArray();
        if (materializedLines.Length == 0) throw new OrdersDomainException(OrdersError.InvalidOrder, "An order requires at least one line.");

        Id = id;
        TenantId = tenantId;
        BranchId = branchId;
        CustomerId = customerId.Trim();
        Currency = currency.ToUpperInvariant();
        PaymentMethod = paymentMethod.Trim().ToUpperInvariant();
        CreatedByUserId = createdByUserId;
        OperationKey = operationKey;
        State = OrderState.Draft;
        CreatedAtUtc = UpdatedAtUtc = now;
        _lines.AddRange(materializedLines);
        TotalCents = checked(materializedLines.Sum(x => x.LineTotalCents));
        _transitions.Add(new OrderTransition(
            initialTransitionId,
            tenantId,
            id,
            null,
            State,
            null,
            createdByUserId,
            now,
            operationKey,
            correlationId));
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public string PaymentMethod { get; private set; } = string.Empty;
    public int TotalCents { get; private set; }
    public OrderState State { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string? OperationKey { get; private set; }
    public uint Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines;
    public IReadOnlyCollection<OrderTransition> Transitions => _transitions;

    public OrderTransition Approve(Guid transitionId, Guid? byUserId, string? reason, DateTimeOffset now, string? operationKey = null, string? correlationId = null) =>
        Transition(OrderState.Approved, transitionId, byUserId, reason, now, operationKey, correlationId);
    public OrderTransition Cancel(Guid transitionId, Guid? byUserId, string? reason, DateTimeOffset now, string? operationKey = null, string? correlationId = null) =>
        Transition(OrderState.Cancelled, transitionId, byUserId, reason, now, operationKey, correlationId);
    public OrderTransition MoveToPicking(Guid transitionId, Guid? byUserId, string? reason, DateTimeOffset now, string? correlationId = null, string? causationId = null, string? source = null) =>
        Transition(OrderState.Picking, transitionId, byUserId, reason, now, null, correlationId, causationId, source);
    public OrderTransition MarkReadyForDeliveryRoute(Guid transitionId, Guid? byUserId, string? reason, DateTimeOffset now, string? correlationId = null, string? causationId = null, string? source = null) =>
        Transition(OrderState.ReadyForDeliveryRoute, transitionId, byUserId, reason, now, null, correlationId, causationId, source);
    public OrderTransition MarkOutForDelivery(Guid transitionId, Guid? byUserId, string? reason, DateTimeOffset now, string? correlationId = null) =>
        Transition(OrderState.OutForDelivery, transitionId, byUserId, reason, now, null, correlationId);
    public OrderTransition MarkDeliveryAttemptFailed(Guid transitionId, Guid? byUserId, string? reason, DateTimeOffset now, string? correlationId = null) =>
        Transition(OrderState.DeliveryAttemptFailed, transitionId, byUserId, reason, now, null, correlationId);
    public OrderTransition MarkDelivered(Guid transitionId, Guid? byUserId, string? reason, DateTimeOffset now, string? correlationId = null) =>
        Transition(OrderState.Delivered, transitionId, byUserId, reason, now, null, correlationId);
    public OrderTransition Settle(Guid transitionId, Guid? byUserId, string? reason, DateTimeOffset now, string? correlationId = null) =>
        Transition(OrderState.Settled, transitionId, byUserId, reason, now, null, correlationId);
    public OrderTransition RequeueForDelivery(Guid transitionId, Guid? byUserId, string? reason, DateTimeOffset now, string? operationKey = null, string? correlationId = null) =>
        Transition(OrderState.ReadyForDeliveryRoute, transitionId, byUserId, reason, now, operationKey, correlationId);

    private OrderTransition Transition(
        OrderState target,
        Guid transitionId,
        Guid? byUserId,
        string? reason,
        DateTimeOffset now,
        string? operationKey,
        string? correlationId,
        string? causationId = null,
        string? source = null)
    {
        if (transitionId == Guid.Empty) throw new OrdersDomainException(OrdersError.InvalidOrder, "Transition id is required.");
        if (!CanTransition(State, target)) throw new OrdersDomainException(OrdersError.InvalidTransition, $"Cannot transition from {State} to {target}.");
        var from = State;
        State = target;
        UpdatedAtUtc = now;
        var transition = new OrderTransition(
            transitionId,
            TenantId,
            Id,
            from,
            target,
            NormalizeReason(reason),
            byUserId,
            now,
            operationKey,
            correlationId,
            causationId,
            source);
        _transitions.Add(transition);
        return transition;
    }

    public static bool CanTransition(OrderState from, OrderState to) => (from, to) switch
    {
        (OrderState.Draft, OrderState.Approved or OrderState.Cancelled) => true,
        (OrderState.Approved, OrderState.Picking or OrderState.Cancelled) => true,
        (OrderState.Picking, OrderState.ReadyForDeliveryRoute) => true,
        (OrderState.ReadyForDeliveryRoute, OrderState.OutForDelivery) => true,
        (OrderState.OutForDelivery, OrderState.Delivered or OrderState.DeliveryAttemptFailed) => true,
        (OrderState.DeliveryAttemptFailed, OrderState.ReadyForDeliveryRoute or OrderState.Cancelled) => true,
        (OrderState.Delivered, OrderState.Settled) => true,
        _ => false,
    };

    private static string? NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}

public sealed class OrderLine
{
    private OrderLine() { }

    public OrderLine(Guid id, Guid orderId, string productId, string productName, int quantity, int unitPriceCents)
    {
        if (id == Guid.Empty) throw new OrdersDomainException(OrdersError.InvalidOrder, "Order line id is required.");
        if (string.IsNullOrWhiteSpace(productId) || productId.Length > 256) throw new OrdersDomainException(OrdersError.InvalidOrder, "productId is invalid.");
        if (string.IsNullOrWhiteSpace(productName) || productName.Length > 512) throw new OrdersDomainException(OrdersError.InvalidOrder, "productName is invalid.");
        if (quantity <= 0) throw new OrdersDomainException(OrdersError.InvalidOrder, "quantity must be positive.");
        if (unitPriceCents < 0) throw new OrdersDomainException(OrdersError.InvalidOrder, "unitPriceCents cannot be negative.");
        Id = id; OrderId = orderId; ProductId = productId.Trim(); ProductName = productName.Trim(); Quantity = quantity; UnitPriceCents = unitPriceCents;
        LineTotalCents = checked(quantity * unitPriceCents);
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string ProductId { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public int UnitPriceCents { get; private set; }
    public int LineTotalCents { get; private set; }
}

public sealed class OrderTransition
{
    private OrderTransition() { }

    public OrderTransition(
        Guid id,
        Guid tenantId,
        Guid orderId,
        OrderState? fromState,
        OrderState toState,
        string? reason,
        Guid? byUserId,
        DateTimeOffset occurredAtUtc,
        string? operationKey = null,
        string? correlationId = null,
        string? causationId = null,
        string? source = null)
    {
        if (id == Guid.Empty) throw new OrdersDomainException(OrdersError.InvalidOrder, "Transition id is required.");
        if (tenantId == Guid.Empty) throw new OrdersDomainException(OrdersError.InvalidOrder, "Tenant is required.");
        Id = id;
        TenantId = tenantId;
        OrderId = orderId;
        FromState = fromState;
        ToState = toState;
        Reason = reason;
        ByUserId = byUserId;
        OccurredAtUtc = occurredAtUtc;
        OperationKey = operationKey;
        CorrelationId = correlationId;
        CausationId = causationId;
        Source = source;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderState? FromState { get; private set; }
    public OrderState ToState { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ByUserId { get; private set; }
    public string? OperationKey { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? CausationId { get; private set; }
    public string? Source { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
}

public static class OrdersError
{
    public const string InvalidOrder = "INVALID_ORDER";
    public const string OrderNotFound = "ORDER_NOT_FOUND";
    public const string InvalidTransition = "INVALID_ORDER_TRANSITION";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string ConcurrencyConflict = "ORDER_CONCURRENCY_CONFLICT";
    public const string IdempotencyKeyConflict = "IDEMPOTENCY_KEY_CONFLICT";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string InvalidCursor = "INVALID_CURSOR";
}

public sealed class OrdersDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
