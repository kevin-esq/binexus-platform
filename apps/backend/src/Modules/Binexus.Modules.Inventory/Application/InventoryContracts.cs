using Binexus.SharedKernel.Results;

namespace Binexus.Modules.Inventory.Application;

public sealed record StockItemSummary(Guid Id, Guid BranchId, string ProductId, int OnHand, int Reserved, int Available, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ListStockItemsQuery(Guid? BranchId, string? ProductId, int? Limit, string? Cursor);
public sealed record ListStockItemsResult(IReadOnlyList<StockItemSummary> Items, string? NextCursor);
public sealed record AdjustStockRequest(Guid BranchId, string ProductId, int Delta, string Reason, string? OperationKey = null);
public sealed record AdjustStockResult(StockItemSummary StockItem, Guid MovementId);
public sealed record StockTransferSummary(Guid Id, Guid SourceBranchId, Guid DestinationBranchId, string ProductId, int Quantity, string Status, string? Reason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ReceivedAt, DateTimeOffset? CancelledAt);
public sealed record CreateStockTransferRequest(Guid SourceBranchId, Guid DestinationBranchId, string ProductId, int Quantity, string? Reason, string? OperationKey = null);
public sealed record ListStockTransfersQuery(string? Status, int? Limit, string? Cursor);
public sealed record ListStockTransfersResult(IReadOnlyList<StockTransferSummary> Items, string? NextCursor);
public sealed record ReceiveStockTransferResult(StockTransferSummary Transfer, Guid SourceMovementId, Guid DestinationMovementId);

public interface IInventoryService
{
    Task<Result<ListStockItemsResult>> ListStockAsync(ListStockItemsQuery query, CancellationToken ct);
    Task<Result<AdjustStockResult>> AdjustAsync(AdjustStockRequest request, CancellationToken ct);
    Task<Result<StockTransferSummary>> CreateTransferAsync(CreateStockTransferRequest request, CancellationToken ct);
    Task<Result<ListStockTransfersResult>> ListTransfersAsync(ListStockTransfersQuery query, CancellationToken ct);
    Task<Result<ReceiveStockTransferResult>> ReceiveTransferAsync(Guid transferId, CancellationToken ct);
    Task<Result<StockTransferSummary>> CancelTransferAsync(Guid transferId, CancellationToken ct);
}
