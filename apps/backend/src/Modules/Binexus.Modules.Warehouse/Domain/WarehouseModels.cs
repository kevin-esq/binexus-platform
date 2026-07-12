using Binexus.SharedKernel.Abstractions;

namespace Binexus.Modules.Warehouse.Domain;

public enum PickingTaskStatus
{
    Pending,
    Completed,
    Cancelled,
}

public sealed class PickingTask : ITenantScoped
{
    private readonly List<PickingLine> _lines = [];

    private PickingTask() { }

    public PickingTask(
        Guid id,
        Guid tenantId,
        Guid branchId,
        Guid orderId,
        Guid createdFromEventId,
        IEnumerable<PickingLine> lines,
        DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Picking task id is required.");
        if (tenantId == Guid.Empty) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Tenant is required.");
        if (branchId == Guid.Empty) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Branch is required.");
        if (orderId == Guid.Empty) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Order is required.");
        if (createdFromEventId == Guid.Empty) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Created event is required.");

        var materializedLines = lines.ToArray();
        if (materializedLines.Length == 0)
        {
            throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Picking task requires at least one line.");
        }

        Id = id;
        TenantId = tenantId;
        BranchId = branchId;
        OrderId = orderId;
        CreatedFromEventId = createdFromEventId;
        Status = PickingTaskStatus.Pending;
        CreatedAtUtc = UpdatedAtUtc = now;
        _lines.AddRange(materializedLines);
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid OrderId { get; private set; }
    public PickingTaskStatus Status { get; private set; }
    public Guid CreatedFromEventId { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public string? CompletionOperationKey { get; private set; }
    public uint Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<PickingLine> Lines => _lines;

    public void Complete(Guid completedByUserId, DateTimeOffset now, string operationKey)
    {
        if (completedByUserId == Guid.Empty)
        {
            throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Completion actor is required.");
        }

        if (string.IsNullOrWhiteSpace(operationKey))
        {
            throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Completion operation key is required.");
        }

        if (Status != PickingTaskStatus.Pending)
        {
            throw new WarehouseDomainException(WarehouseError.PickingTaskNotPending, "Picking task is not pending.");
        }

        foreach (var line in _lines)
        {
            line.MarkFullyPicked();
        }

        Status = PickingTaskStatus.Completed;
        CompletedAtUtc = now;
        CompletedByUserId = completedByUserId;
        CompletionOperationKey = operationKey;
        UpdatedAtUtc = now;
    }
}

public sealed class PickingLine : ITenantScoped
{
    private PickingLine() { }

    public PickingLine(
        Guid id,
        Guid tenantId,
        Guid pickingTaskId,
        Guid orderLineId,
        string productId,
        int quantity)
    {
        if (id == Guid.Empty) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Picking line id is required.");
        if (tenantId == Guid.Empty) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Tenant is required.");
        if (pickingTaskId == Guid.Empty) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Picking task is required.");
        if (orderLineId == Guid.Empty) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "Order line is required.");
        if (string.IsNullOrWhiteSpace(productId) || productId.Length > 256) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "productId is invalid.");
        if (quantity <= 0) throw new WarehouseDomainException(WarehouseError.InvalidPickingTask, "quantity must be positive.");

        Id = id;
        TenantId = tenantId;
        PickingTaskId = pickingTaskId;
        OrderLineId = orderLineId;
        ProductId = productId.Trim();
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PickingTaskId { get; private set; }
    public Guid OrderLineId { get; private set; }
    public string ProductId { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public int PickedQuantity { get; private set; }

    public void MarkFullyPicked() => PickedQuantity = Quantity;
}

public static class WarehouseError
{
    public const string InvalidPickingTask = "INVALID_PICKING_TASK";
    public const string PickingTaskNotFound = "PICKING_TASK_NOT_FOUND";
    public const string PickingTaskNotPending = "PICKING_TASK_NOT_PENDING";
    public const string InvalidCursor = "INVALID_CURSOR";
}

public sealed class WarehouseDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
