using Binexus.Modules.Inventory.Domain;
using Binexus.Modules.Inventory.Infrastructure;
using FluentAssertions;

namespace Binexus.UnitTests.Inventory;

public sealed class InventoryPersistedEnumsTests
{
    [Theory]
    [InlineData(StockTransferStatus.Pending, "PENDING")]
    [InlineData(StockTransferStatus.Received, "RECEIVED")]
    [InlineData(StockTransferStatus.Cancelled, "CANCELLED")]
    public void Transfer_status_roundtrips(StockTransferStatus status, string persisted)
    {
        InventoryPersistedEnums.ToPersisted(status).Should().Be(persisted);
        InventoryPersistedEnums.ParseTransferStatus(persisted).Should().Be(status);
        InventoryPersistedEnums.ToApi(status).Should().Be(persisted);
    }

    [Theory]
    [InlineData(StockReservationStatus.Active, "ACTIVE")]
    [InlineData(StockReservationStatus.Released, "RELEASED")]
    [InlineData(StockReservationStatus.Failed, "FAILED")]
    public void Reservation_status_roundtrips(StockReservationStatus status, string persisted)
    {
        InventoryPersistedEnums.ToPersisted(status).Should().Be(persisted);
        InventoryPersistedEnums.ParseReservationStatus(persisted).Should().Be(status);
    }

    [Theory]
    [InlineData(StockMovementType.Reserve, "RESERVE")]
    [InlineData(StockMovementType.TransferOut, "TRANSFER_OUT")]
    [InlineData(StockMovementType.Sale, "SALE")]
    public void Movement_type_roundtrips(StockMovementType type, string persisted)
    {
        InventoryPersistedEnums.ToPersisted(type).Should().Be(persisted);
        InventoryPersistedEnums.ParseMovementType(persisted).Should().Be(type);
    }
}
