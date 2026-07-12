namespace Binexus.Modules.Inventory.Contracts;

/// <summary>
/// Public Inventory ports for Orders/Sales. Owned by Inventory; no SharedKernel residency.
/// </summary>
public sealed record InventoryReservationLine(Guid BranchId, Guid OrderLineId, string ProductId, int Quantity);

public sealed record InventoryReserveForOrderRequest(
    Guid TenantId,
    Guid OrderId,
    IReadOnlyList<InventoryReservationLine> Lines,
    string? CorrelationId = null);

public sealed record InventoryReservationResult(bool Succeeded, string? FailureCode);

public sealed record InventoryReleaseForOrderRequest(
    Guid TenantId,
    Guid OrderId,
    string? CorrelationId = null);

public sealed record InventorySaleLine(Guid BranchId, Guid SaleLineId, string ProductId, int Quantity);

public sealed record InventorySaleDecrementRequest(
    Guid TenantId,
    Guid SaleId,
    IReadOnlyList<InventorySaleLine> Lines);

public sealed record InventorySaleDecrementResult(bool Succeeded, string? FailureCode);

public interface IInventoryReservationApi
{
    Task<InventoryReservationResult> TryReserveForOrderAsync(
        InventoryReserveForOrderRequest request,
        CancellationToken ct);

    Task ReleaseForOrderAsync(InventoryReleaseForOrderRequest request, CancellationToken ct);
}

public interface IInventorySaleApi
{
    Task<InventorySaleDecrementResult> DecrementForSaleAsync(
        InventorySaleDecrementRequest request,
        CancellationToken ct);
}
