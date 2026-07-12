using Binexus.Modules.Orders.Domain;
using FluentAssertions;

namespace Binexus.UnitTests.Orders;

public sealed class OrderDomainTests
{
    [Fact]
    public void Create_calculates_total_and_records_draft_transition()
    {
        var order = Create();

        order.TotalCents.Should().Be(550);
        order.State.Should().Be(OrderState.Draft);
        order.Transitions.Should().ContainSingle(t => t.FromState == null && t.ToState == OrderState.Draft);
        order.Transitions.Single().TenantId.Should().Be(order.TenantId);
    }

    [Fact]
    public void State_machine_allows_delivery_failure_requeue_then_cancel()
    {
        var order = Create();
        var actor = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        order.Approve(Guid.CreateVersion7(), actor, null, now);
        order.MoveToPicking(Guid.CreateVersion7(), actor, null, now);
        order.MarkReadyForDeliveryRoute(Guid.CreateVersion7(), actor, null, now);
        order.MarkOutForDelivery(Guid.CreateVersion7(), actor, null, now);
        order.MarkDeliveryAttemptFailed(Guid.CreateVersion7(), actor, "recipient unavailable", now);
        order.RequeueForDelivery(Guid.CreateVersion7(), actor, null, now);
        order.MarkOutForDelivery(Guid.CreateVersion7(), actor, null, now);
        order.MarkDeliveryAttemptFailed(Guid.CreateVersion7(), actor, null, now);
        order.Cancel(Guid.CreateVersion7(), actor, "cancelled by customer", now);

        order.State.Should().Be(OrderState.Cancelled);
        order.Transitions.Should().HaveCount(10);
    }

    [Fact]
    public void State_machine_rejects_approve_after_approval()
    {
        var order = Create();
        var actor = Guid.CreateVersion7();
        order.Approve(Guid.CreateVersion7(), actor, null, DateTimeOffset.UtcNow);

        var action = () => order.Approve(Guid.CreateVersion7(), actor, null, DateTimeOffset.UtcNow);

        action.Should().Throw<OrdersDomainException>().Which.Code.Should().Be(OrdersError.InvalidTransition);
    }

    [Fact]
    public void Create_rejects_empty_lines_zero_quantity_negative_price_and_bad_currency()
    {
        var id = Guid.CreateVersion7();
        var actEmpty = () => new Order(
            id, Guid.CreateVersion7(), Guid.CreateVersion7(), "c", "USD", "CASH", Guid.CreateVersion7(),
            Guid.CreateVersion7(), [], DateTimeOffset.UtcNow);
        actEmpty.Should().Throw<OrdersDomainException>().Which.Code.Should().Be(OrdersError.InvalidOrder);

        var actQty = () => new OrderLine(Guid.CreateVersion7(), id, "p", "n", 0, 10);
        actQty.Should().Throw<OrdersDomainException>();

        var actPrice = () => new OrderLine(Guid.CreateVersion7(), id, "p", "n", 1, -1);
        actPrice.Should().Throw<OrdersDomainException>();

        var actCurrency = () => new Order(
            id, Guid.CreateVersion7(), Guid.CreateVersion7(), "c", "US", "CASH", Guid.CreateVersion7(),
            Guid.CreateVersion7(), [new OrderLine(Guid.CreateVersion7(), id, "p", "n", 1, 1)], DateTimeOffset.UtcNow);
        actCurrency.Should().Throw<OrdersDomainException>();
    }

    [Fact]
    public void Line_and_order_totals_use_checked_arithmetic()
    {
        var id = Guid.CreateVersion7();
        var actLine = () => new OrderLine(Guid.CreateVersion7(), id, "p", "n", int.MaxValue, 2);
        actLine.Should().Throw<OverflowException>();

        var actTotal = () => new Order(
            id,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "c",
            "USD",
            "CASH",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            [
                new OrderLine(Guid.CreateVersion7(), id, "a", "A", 1, int.MaxValue - 1),
                new OrderLine(Guid.CreateVersion7(), id, "b", "B", 1, 2),
            ],
            DateTimeOffset.UtcNow);
        actTotal.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Transition_requires_non_empty_transition_id()
    {
        var order = Create();
        var act = () => order.Approve(Guid.Empty, Guid.CreateVersion7(), null, DateTimeOffset.UtcNow);
        act.Should().Throw<OrdersDomainException>().Which.Code.Should().Be(OrdersError.InvalidOrder);
    }

    private static Order Create()
    {
        var id = Guid.CreateVersion7();
        return new Order(
            id,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "customer-1",
            "USD",
            "CASH",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            [new OrderLine(Guid.CreateVersion7(), id, "product-1", "Product", 2, 275)],
            DateTimeOffset.UtcNow);
    }
}
