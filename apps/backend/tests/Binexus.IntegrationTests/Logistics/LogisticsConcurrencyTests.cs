using Binexus.Modules.Logistics.Domain;
using FluentAssertions;

namespace Binexus.IntegrationTests.Logistics;

/// <summary>
/// Aggregate state-machine invariants. True thread races are not enforced in-memory
/// without locks; HTTP/DB races are covered by the transactional integration suite.
/// </summary>
[Collection("postgres")]
public sealed class LogisticsConcurrencyTests
{
    [Fact]
    public void Dual_dispatch_second_call_is_idempotent_or_rejected_after_first()
    {
        var route = RouteWithOneStop(out _);
        route.Dispatch(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "dispatch-a");
        var second = () => route.Dispatch(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "dispatch-b");

        // Already dispatched: second call must not change the operation key / must throw or no-op.
        try
        {
            second();
        }
        catch (LogisticsDomainException)
        {
            // expected rejection path
        }

        route.Status.Should().Be(DeliveryRouteStatus.Dispatched);
        route.DispatchOperationKey.Should().Be("dispatch-a");
    }

    [Fact]
    public void Assign_second_call_rejects_when_already_assigned()
    {
        var candidate = new DeliveryRouteCandidate(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);
        candidate.Assign(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        var act = () => candidate.Assign(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        act.Should().Throw<LogisticsDomainException>()
            .Which.Code.Should().Be(LogisticsError.CandidateNotReady);
        candidate.Status.Should().Be(DeliveryRouteCandidateStatus.Assigned);
    }

    [Fact]
    public void Terminal_stop_second_transition_rejects_after_first()
    {
        var route = RouteWithOneStop(out var stop);
        route.Dispatch(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "dispatch");
        stop.Confirm(DateTimeOffset.UtcNow, "confirm");

        var act = () => stop.Fail(DeliveryFailureReason.NoRecipient, null, DateTimeOffset.UtcNow, "fail");

        act.Should().Throw<LogisticsDomainException>()
            .Which.Code.Should().Be(LogisticsError.StopNotPlanned);
        route.CompleteIfTerminal(DateTimeOffset.UtcNow);

        stop.Status.Should().Be(DeliveryRouteStopStatus.Delivered);
        route.Status.Should().Be(DeliveryRouteStatus.Completed);
    }

    private static DeliveryRoute RouteWithOneStop(out DeliveryRouteStop stop)
    {
        var now = DateTimeOffset.UtcNow;
        var route = new DeliveryRoute(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, now, "create");
        stop = new DeliveryRouteStop(Guid.CreateVersion7(), route.TenantId, route.BranchId, route.Id, Guid.CreateVersion7(), 1, now);
        route.AddStop(stop, now, "assign");
        return route;
    }
}
