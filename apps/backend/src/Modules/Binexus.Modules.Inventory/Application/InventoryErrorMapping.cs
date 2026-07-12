using Binexus.Modules.Inventory.Domain;
using Binexus.SharedKernel.Results;

namespace Binexus.Modules.Inventory.Application;

public static class InventoryErrorMapping
{
    public static DomainError ToDomainError(InventoryDomainException ex) =>
        ex.Code switch
        {
            InventoryError.TransferNotFound => DomainError.NotFound(ex.Code, ex.Message),
            InventoryError.ConcurrencyConflict or InventoryError.IdempotencyKeyConflict
                or InventoryError.TransferNotPending or InventoryError.InsufficientStock =>
                DomainError.Conflict(ex.Code, ex.Message),
            "FORBIDDEN" => DomainError.Forbidden(ex.Code, ex.Message),
            _ => DomainError.Validation(ex.Code, ex.Message),
        };
}
