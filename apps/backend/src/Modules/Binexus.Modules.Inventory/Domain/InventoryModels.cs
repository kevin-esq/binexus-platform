using Binexus.SharedKernel.Abstractions;

namespace Binexus.Modules.Inventory.Domain;

public enum StockReservationStatus { Active, Released, Failed }
public enum StockMovementType { Reserve, Release, Adjustment, TransferOut, TransferIn, Sale }
public enum StockTransferStatus { Pending, Received, Cancelled }

public sealed class StockItem : ITenantScoped
{
    private StockItem() { }
    public StockItem(Guid id, Guid tenantId, Guid branchId, string productId, int onHand, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(onHand);
        Id = id; TenantId = tenantId; BranchId = branchId; ProductId = productId; OnHand = onHand; CreatedAtUtc = UpdatedAtUtc = now;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string ProductId { get; private set; } = string.Empty;
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public int Available => checked(OnHand - Reserved);
    public uint Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public void Adjust(int delta, DateTimeOffset now)
    {
        var next = checked(OnHand + delta);
        if (next < Reserved) throw new InventoryDomainException(InventoryError.InvalidAdjustment, "Adjustment would reduce stock below reserved quantity.");
        OnHand = next; UpdatedAtUtc = now;
    }
    public void Reserve(int quantity, DateTimeOffset now)
    {
        RequirePositive(quantity);
        if (Available < quantity) throw new InventoryDomainException(InventoryError.InsufficientStock, "Insufficient stock.");
        Reserved = checked(Reserved + quantity); UpdatedAtUtc = now;
    }
    public void Release(int quantity, DateTimeOffset now)
    {
        RequirePositive(quantity);
        if (Reserved < quantity) throw new InventoryDomainException(InventoryError.InvalidAdjustment, "Cannot release more stock than is reserved.");
        Reserved -= quantity; UpdatedAtUtc = now;
    }
    public void Sell(int quantity, DateTimeOffset now)
    {
        RequirePositive(quantity);
        if (Available < quantity) throw new InventoryDomainException(InventoryError.InsufficientStock, "Insufficient stock.");
        OnHand -= quantity; UpdatedAtUtc = now;
    }
    public void ReceiveTransferOut(int quantity, DateTimeOffset now) { Release(quantity, now); OnHand = checked(OnHand - quantity); UpdatedAtUtc = now; }
    private static void RequirePositive(int quantity) { if (quantity <= 0) throw new InventoryDomainException(InventoryError.ValidationQuantity, "Quantity must be greater than zero."); }
}

public sealed class StockReservation : ITenantScoped
{
    private StockReservation() { }
    public StockReservation(Guid id, Guid tenantId, Guid branchId, Guid orderId, Guid orderLineId, string productId, int quantity, StockReservationStatus status, DateTimeOffset now)
    { Id = id; TenantId = tenantId; BranchId = branchId; OrderId = orderId; OrderLineId = orderLineId; ProductId = productId; Quantity = quantity; Status = status; CreatedAtUtc = UpdatedAtUtc = now; }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid OrderLineId { get; private set; }
    public string ProductId { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public StockReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public void Release(DateTimeOffset now) { if (Status == StockReservationStatus.Active) { Status = StockReservationStatus.Released; UpdatedAtUtc = now; } }
}

public sealed class StockMovement : ITenantScoped
{
    private StockMovement() { }
    public StockMovement(Guid id, Guid tenantId, Guid branchId, string productId, int quantity, StockMovementType type, string? operationKey, DateTimeOffset now)
    { Id = id; TenantId = tenantId; BranchId = branchId; ProductId = productId; Quantity = quantity; Type = type; OperationKey = operationKey; CreatedAtUtc = now; }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string ProductId { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public StockMovementType Type { get; private set; }
    public string? OperationKey { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}

public sealed class StockTransfer : ITenantScoped
{
    private StockTransfer() { }
    public StockTransfer(Guid id, Guid tenantId, Guid sourceBranchId, Guid destinationBranchId, string productId, int quantity, string? reason, DateTimeOffset now)
    {
        if (quantity <= 0 || sourceBranchId == destinationBranchId) throw new InventoryDomainException(InventoryError.ValidationTransfer, "Transfer requires a positive quantity and distinct branches.");
        Id = id; TenantId = tenantId; SourceBranchId = sourceBranchId; DestinationBranchId = destinationBranchId; ProductId = productId; Quantity = quantity; Reason = reason; CreatedAtUtc = UpdatedAtUtc = now;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceBranchId { get; private set; }
    public Guid DestinationBranchId { get; private set; }
    public string ProductId { get; private set; } = string.Empty; public int Quantity { get; private set; }
    public string? Reason { get; private set; }
    public string? OperationKey { get; private set; }
    public StockTransferStatus Status { get; private set; } = StockTransferStatus.Pending; public uint Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ReceivedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public void AssignOperationKey(string operationKey) => OperationKey = operationKey;
    public void Receive(DateTimeOffset now) { EnsurePending(); Status = StockTransferStatus.Received; ReceivedAtUtc = UpdatedAtUtc = now; }
    public void Cancel(DateTimeOffset now) { EnsurePending(); Status = StockTransferStatus.Cancelled; CancelledAtUtc = UpdatedAtUtc = now; }
    private void EnsurePending() { if (Status != StockTransferStatus.Pending) throw new InventoryDomainException(InventoryError.TransferNotPending, "Transfer is not pending."); }
}

public static class InventoryError
{
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string InvalidAdjustment = "INVALID_ADJUSTMENT";
    public const string TransferNotPending = "TRANSFER_NOT_PENDING";
    public const string TransferNotFound = "TRANSFER_NOT_FOUND";
    public const string ConcurrencyConflict = "INVENTORY_CONCURRENCY_CONFLICT";
    public const string IdempotencyKeyConflict = "IDEMPOTENCY_KEY_CONFLICT";
    public const string ValidationQuantity = "VALIDATION_QUANTITY";
    public const string ValidationTransfer = "VALIDATION_TRANSFER";
    public const string InvalidCursor = "INVALID_CURSOR";
}
public sealed class InventoryDomainException(string code, string message) : Exception(message) { public string Code { get; } = code; }
