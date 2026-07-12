using Binexus.IntegrationTests.Infrastructure;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Binexus.IntegrationTests.Outbox;

[Collection("postgres")]
public sealed class HandlerAtomicityTests : IClassFixture<PostgresTestFixture>
{
    private readonly PostgresTestFixture _fixture;

    public HandlerAtomicityTests(PostgresTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Handler_failure_before_commit_rolls_back_business_effects_and_delivery()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("ATOMIC_FAIL_EVENT", "test.atomic.fail");

        var messageId = await SeedMessage(tenantId, "ATOMIC_FAIL_EVENT", registry);
        await RunAtomicProcessor("worker-1", registry, "test.atomic.fail", "ATOMIC_FAIL_EVENT", shouldSucceed: false);

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == messageId))
            .PayloadJson.Should().Be("{}");
        (await db.EventHandlerDeliveries.AsNoTracking().FirstAsync(d => d.EventId == messageId))
            .Status.Should().Be(EventHandlerDeliveryStatus.FailedTransient);
    }

    [Fact]
    public async Task Handler_success_commits_business_effects_and_processed_delivery()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("ATOMIC_OK_EVENT", "test.atomic.ok");

        var messageId = await SeedMessage(tenantId, "ATOMIC_OK_EVENT", registry);
        var firstRun = await RunAtomicProcessor("worker-1", registry, "test.atomic.ok", "ATOMIC_OK_EVENT", shouldSucceed: true);
        var secondRun = await RunAtomicProcessor("worker-2", registry, "test.atomic.ok", "ATOMIC_OK_EVENT", shouldSucceed: true);

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var committed = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == messageId);
        committed.PayloadJson.Should().Contain("probe");
        committed.PayloadJson.Should().Contain("seen");
        (await db.EventHandlerDeliveries.AsNoTracking().FirstAsync(d => d.EventId == messageId))
            .Status.Should().Be(EventHandlerDeliveryStatus.Processed);

        firstRun.ProcessCount.Should().Be(1);
        secondRun.ProcessCount.Should().Be(0);
    }

    [Fact]
    public async Task Mixed_permanent_and_transient_deliveries_keep_outbox_processing()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("MIXED_EVENT", "test.perm", "test.transient");
        var permanent = new PermanentFailureProcessor("test.perm", "MIXED_EVENT");
        var transient = new TransientThenSuccessProcessor("test.transient", "MIXED_EVENT");
        TransientThenSuccessProcessor.Reset("test.transient");

        var messageId = await SeedMessage(tenantId, "MIXED_EVENT", registry, permanent, transient);
        await RunProcessor("worker-1", registry, permanent, transient);

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == messageId))
            .Status.Should().BeOneOf(OutboxMessageStatus.Processing, OutboxMessageStatus.CompletedWithFailures);
        (await db.EventHandlerDeliveries.AsNoTracking().Where(d => d.EventId == messageId).ToListAsync())
            .Should().Contain(d => d.Status == EventHandlerDeliveryStatus.FailedPermanent);
    }

    [Fact]
    public async Task Delivery_with_active_lock_is_not_reclaimed()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("LOCKED_DELIVERY_EVENT", "test.locked");
        var handler = new CountingTestProcessor("test.locked", "LOCKED_DELIVERY_EVENT");
        var messageId = await SeedMessage(tenantId, "LOCKED_DELIVERY_EVENT", registry, handler);

        using (var scope = _fixture.CreateScope(services => ConfigureServices(services, registry, handler)))
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var message = await db.OutboxMessages.FirstAsync(m => m.Id == messageId);
            message.Status = OutboxMessageStatus.Processing;
            message.InitializedAtUtc = DateTimeOffset.UtcNow;
            message.ApplicableHandlerKeys = ["test.locked"];
            db.EventHandlerDeliveries.Add(new EventHandlerDelivery
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                EventId = messageId,
                HandlerKey = "test.locked",
                Status = EventHandlerDeliveryStatus.Processing,
                LockedBy = "busy-worker",
                LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await RunProcessor("worker-2", registry, handler);
        handler.ProcessCount.Should().Be(0);
    }

    [Fact]
    public async Task Applicable_handler_snapshot_is_immutable_after_first_claim()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("SNAPSHOT_EVENT", "test.snapshot");
        var handler = new CountingTestProcessor("test.snapshot", "SNAPSHOT_EVENT");
        var messageId = await SeedMessage(tenantId, "SNAPSHOT_EVENT", registry, handler);

        await RunProcessor("worker-1", registry, handler);
        registry.SetHandlers("SNAPSHOT_EVENT", "test.other");

        await RunProcessor("worker-2", registry, handler);

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var message = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == messageId);
        message.ApplicableHandlerKeys.Should().Equal("test.snapshot");
        (await db.EventHandlerDeliveries.AsNoTracking().CountAsync(d => d.EventId == messageId)).Should().Be(1);
    }

    private async Task<Guid> SeedMessage(
        Guid tenantId,
        string eventName,
        ConfigurableEventHandlerRegistry registry,
        params IIntegrationEventProcessor[] processors)
    {
        using var scope = _fixture.CreateScope(services => ConfigureServices(services, registry, processors));
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var id = Guid.CreateVersion7();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = id,
            TenantId = tenantId,
            EventName = eventName,
            PayloadJson = "{}",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Pending,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<AtomicProbeProcessor> RunAtomicProcessor(
        string workerId,
        ConfigurableEventHandlerRegistry registry,
        string handlerKey,
        string eventName,
        bool shouldSucceed)
    {
        AtomicProbeProcessor? handler = null;
        using var scope = _fixture.CreateScope(services =>
        {
            ConfigureServices(services, registry);
            services.AddSingleton<IIntegrationEventProcessor>(sp =>
            {
                handler = new AtomicProbeProcessor(handlerKey, eventName, shouldSucceed);
                return handler;
            });
        });

        var outboxProcessor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        await outboxProcessor.ProcessBatchAsync(workerId, CancellationToken.None);
        return handler!;
    }

    private async Task RunProcessor(
        string workerId,
        ConfigurableEventHandlerRegistry registry,
        params IIntegrationEventProcessor[] processors)
    {
        using var scope = _fixture.CreateScope(services => ConfigureServices(services, registry, processors));
        var outboxProcessor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        await outboxProcessor.ProcessBatchAsync(workerId, CancellationToken.None);
    }

    private static void ConfigureServices(
        IServiceCollection services,
        ConfigurableEventHandlerRegistry registry,
        params IIntegrationEventProcessor[] processors)
    {
        services.Replace(ServiceDescriptor.Singleton<IEventHandlerRegistry>(registry));
        services.RemoveAll<IIntegrationEventProcessor>();
        foreach (var processor in processors)
        {
            services.AddSingleton<IIntegrationEventProcessor>(processor);
        }
    }
}
