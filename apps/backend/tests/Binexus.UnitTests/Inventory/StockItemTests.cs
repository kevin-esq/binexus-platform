using Binexus.Modules.Inventory.Domain;
using FluentAssertions;

namespace Binexus.UnitTests.Inventory;

public sealed class StockItemTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Available_is_computed_not_stored()
    {
        var item = new StockItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "sku", 10, Now);
        item.Reserve(4, Now);
        item.Available.Should().Be(6);
    }

    [Fact]
    public void Adjust_cannot_reduce_below_reservations()
    {
        var item = new StockItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "sku", 10, Now);
        item.Reserve(8, Now);
        var action = () => item.Adjust(-3, Now);
        action.Should().Throw<InventoryDomainException>().Which.Code.Should().Be(InventoryError.InvalidAdjustment);
    }

    [Fact]
    public void Sale_only_consumes_available_stock()
    {
        var item = new StockItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "sku", 10, Now);
        item.Reserve(8, Now);
        var action = () => item.Sell(3, Now);
        action.Should().Throw<InventoryDomainException>().Which.Code.Should().Be(InventoryError.InsufficientStock);
    }

    [Fact]
    public void Transfer_only_transitions_from_pending()
    {
        var transfer = new StockTransfer(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "sku", 1, null, Now);
        transfer.Receive(Now);
        var action = () => transfer.Cancel(Now);
        action.Should().Throw<InventoryDomainException>().Which.Code.Should().Be(InventoryError.TransferNotPending);
    }

    [Fact]
    public void Quantity_and_branch_invariants_are_enforced()
    {
        var sameBranch = Guid.NewGuid();
        var action = () => new StockTransfer(Guid.NewGuid(), Guid.NewGuid(), sameBranch, sameBranch, "sku", 0, null, Now);
        action.Should().Throw<InventoryDomainException>().Which.Code.Should().Be(InventoryError.ValidationTransfer);
    }
}
