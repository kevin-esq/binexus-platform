using Binexus.Modules.Warehouse.Domain;
using Binexus.SharedKernel.Abstractions;
using Binexus.SharedKernel.Results;

namespace Binexus.Modules.Warehouse.Application;

public sealed record PickingLineSummary(Guid Id, Guid OrderLineId, string ProductId, int Quantity, int PickedQuantity);

public sealed record PickingTaskSummary(
    Guid Id,
    Guid BranchId,
    Guid OrderId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    Guid? CompletedByUserId,
    IReadOnlyList<PickingLineSummary> Lines);

public sealed record ListPickingTasksQuery(string? Status, Guid? BranchId, int? Limit, string? Cursor);

public sealed record ListPickingTasksResult(IReadOnlyList<PickingTaskSummary> Items, string? NextCursor);

public sealed record CompletePickingTaskCommand(Guid PickingTaskId, string OperationKey, string? CorrelationId) : ITransactionalCommand;

public interface IWarehouseQueryService
{
    Task<Result<ListPickingTasksResult>> ListAsync(ListPickingTasksQuery query, CancellationToken ct);

    Task<Result<PickingTaskSummary>> GetAsync(Guid pickingTaskId, CancellationToken ct);
}

public static class WarehouseErrorMapping
{
    public static DomainError ToDomainError(WarehouseDomainException ex) =>
        ex.Code switch
        {
            WarehouseError.PickingTaskNotFound => DomainError.NotFound(ex.Code, ex.Message),
            WarehouseError.PickingTaskNotPending => DomainError.Conflict(ex.Code, ex.Message),
            "FORBIDDEN" => DomainError.Forbidden(ex.Code, ex.Message),
            _ => DomainError.Validation(ex.Code, ex.Message),
        };
}
