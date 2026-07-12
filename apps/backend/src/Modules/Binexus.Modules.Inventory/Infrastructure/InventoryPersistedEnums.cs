using Binexus.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Binexus.Modules.Inventory.Infrastructure;

public static class InventoryPersistedEnums
{
    public static readonly ValueConverter<StockReservationStatus, string> ReservationStatusConverter =
        new(
            status => ToPersisted(status),
            value => ParseReservationStatus(value));

    public static readonly ValueConverter<StockMovementType, string> MovementTypeConverter =
        new(
            type => ToPersisted(type),
            value => ParseMovementType(value));

    public static readonly ValueConverter<StockTransferStatus, string> TransferStatusConverter =
        new(
            status => ToPersisted(status),
            value => ParseTransferStatus(value));

    public static string ToApi(StockTransferStatus status) => ToPersisted(status);
    public static string ToPersisted(StockReservationStatus status) => status switch
    {
        StockReservationStatus.Active => "ACTIVE",
        StockReservationStatus.Released => "RELEASED",
        StockReservationStatus.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string ToPersisted(StockMovementType type) => type switch
    {
        StockMovementType.Reserve => "RESERVE",
        StockMovementType.Release => "RELEASE",
        StockMovementType.Adjustment => "ADJUSTMENT",
        StockMovementType.TransferOut => "TRANSFER_OUT",
        StockMovementType.TransferIn => "TRANSFER_IN",
        StockMovementType.Sale => "SALE",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string ToPersisted(StockTransferStatus status) => status switch
    {
        StockTransferStatus.Pending => "PENDING",
        StockTransferStatus.Received => "RECEIVED",
        StockTransferStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static StockReservationStatus ParseReservationStatus(string value) => value switch
    {
        "ACTIVE" => StockReservationStatus.Active,
        "RELEASED" => StockReservationStatus.Released,
        "FAILED" => StockReservationStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown persisted reservation status."),
    };

    public static StockMovementType ParseMovementType(string value) => value switch
    {
        "RESERVE" => StockMovementType.Reserve,
        "RELEASE" => StockMovementType.Release,
        "ADJUSTMENT" => StockMovementType.Adjustment,
        "TRANSFER_OUT" => StockMovementType.TransferOut,
        "TRANSFER_IN" => StockMovementType.TransferIn,
        "SALE" => StockMovementType.Sale,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown persisted movement type."),
    };

    public static StockTransferStatus ParseTransferStatus(string value) => value switch
    {
        "PENDING" => StockTransferStatus.Pending,
        "RECEIVED" => StockTransferStatus.Received,
        "CANCELLED" => StockTransferStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown persisted transfer status."),
    };
}
