using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Logistics.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Logistics;

[Collection("postgres")]
public sealed class CandidateRequeueTests(PostgresTestFixture postgres) : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Failed_delivery_requeue_reopens_candidate_and_preserves_historical_failed_stop()
    {
        await postgres.ResetOutboxAsync();
        var context = await AcmeContextAsync();
        var orderId = Guid.CreateVersion7();
        var oldRouteId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        using (var scope = postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var candidate = new DeliveryRouteCandidate(ids.NewId(), context.TenantId, orderId, context.BranchId, ids.NewId(), now);
            var route = new DeliveryRoute(oldRouteId, context.TenantId, context.BranchId, null, now, $"old-{Guid.NewGuid():N}");
            var stop = new DeliveryRouteStop(ids.NewId(), context.TenantId, context.BranchId, oldRouteId, orderId, 1, now);
            candidate.Assign(oldRouteId, now);
            route.AddStop(stop, now, $"assign-{Guid.NewGuid():N}");
            route.Dispatch(context.UserId, now, $"dispatch-{Guid.NewGuid():N}");
            stop.Fail(DeliveryFailureReason.NoRecipient, "no answer", now, $"fail-{Guid.NewGuid():N}");
            route.CompleteIfTerminal(now);
            db.AddRange(candidate, route, stop);
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = ids.NewId(),
                TenantId = context.TenantId,
                EventName = "ORDER_READY_FOR_DELIVERY_ROUTE",
                PayloadJson = JsonSerializer.Serialize(new { orderId, branchId = context.BranchId }),
                OccurredAtUtc = now,
                CreatedAtUtc = now,
            });
            await db.SaveChangesAsync();
        }

        using (var scope = postgres.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
            (await processor.ProcessBatchAsync($"requeue-{Guid.NewGuid():N}", CancellationToken.None)).Should().Be(1);
        }

        using var verify = postgres.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var candidateAfter = await verifyDb.Set<DeliveryRouteCandidate>().IgnoreQueryFilters().SingleAsync(x => x.OrderId == orderId);
        var historicalStop = await verifyDb.Set<DeliveryRouteStop>().IgnoreQueryFilters().SingleAsync(x => x.DeliveryRouteId == oldRouteId && x.OrderId == orderId);

        candidateAfter.Status.Should().Be(DeliveryRouteCandidateStatus.Ready);
        candidateAfter.DeliveryRouteId.Should().BeNull();
        historicalStop.Status.Should().Be(DeliveryRouteStopStatus.Failed);
        historicalStop.FailureReason.Should().Be(DeliveryFailureReason.NoRecipient);
    }

    private async Task<(Guid TenantId, Guid BranchId, Guid UserId)> AcmeContextAsync()
    {
        using var scope = postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Main");
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.NormalizedEmail == EmailNormalizer.Normalize("admin@acme.test"));
        return (tenant.Id, branch.Id, user.Id);
    }
}
