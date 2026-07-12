using Binexus.IntegrationTests.Infrastructure;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Binexus.IntegrationTests.Outbox;

[Collection("postgres")]
public sealed class OutboxProcessorTests : IClassFixture<PostgresTestFixture>
{
    private readonly PostgresTestFixture _fixture;

    public OutboxProcessorTests(PostgresTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Event_without_handlers_completes_immediately()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("NO_HANDLERS_EVENT");

        var messageId = await SeedMessage(tenantId, "NO_HANDLERS_EVENT", registry);
        await RunProcessor("worker-a", registry);

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var message = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == messageId);
        message.Status.Should().Be(OutboxMessageStatus.Completed);
        message.InitializedAtUtc.Should().NotBeNull();
        (await db.EventHandlerDeliveries.CountAsync(d => d.EventId == messageId)).Should().Be(0);
    }

    [Fact]
    public async Task Two_workers_do_not_duplicate_deliveries()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("DUAL_WORKER_EVENT", "test.handler");
        var handler = new CountingTestProcessor("test.handler", "DUAL_WORKER_EVENT");

        var messageId = await SeedMessage(tenantId, "DUAL_WORKER_EVENT", registry, handler);
        await Task.WhenAll(
            RunProcessor("worker-1", registry, handler),
            RunProcessor("worker-2", registry, handler));

        handler.ProcessCount.Should().Be(1);
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.EventHandlerDeliveries.AsNoTracking().CountAsync(d => d.EventId == messageId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Two_workers_process_concurrent_batch_without_duplicate_initialization()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("BATCH_EVENT", "test.batch");
        var handler = new CountingTestProcessor("test.batch", "BATCH_EVENT");

        var messageIds = await Task.WhenAll(
            Enumerable.Range(0, 5)
                .Select(_ => SeedMessage(tenantId, "BATCH_EVENT", registry, handler))
                .ToArray());

        await Task.WhenAll(
            RunProcessor("worker-a", registry, handler),
            RunProcessor("worker-b", registry, handler));

        // Concurrent claim can leave a row locked/unclaimed for one batch; drain until complete.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var checkScope = _fixture.CreateScope();
            var checkDb = checkScope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var pending = await checkDb.OutboxMessages.AsNoTracking()
                .CountAsync(m => messageIds.Contains(m.Id) && m.Status != OutboxMessageStatus.Completed);
            if (pending == 0)
            {
                break;
            }

            await RunProcessor($"worker-drain-{attempt}", registry, handler);
        }

        handler.ProcessCount.Should().Be(5);
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.EventHandlerDeliveries.AsNoTracking().CountAsync(d => messageIds.Contains(d.EventId)))
            .Should().Be(5);
        (await db.OutboxMessages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .Select(m => m.Status)
            .ToListAsync())
            .Should().AllBeEquivalentTo(OutboxMessageStatus.Completed);
    }

    [Fact]
    public async Task Processed_handler_is_not_executed_again()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("IDEMPOTENT_EVENT", "test.idempotent");
        var handler = new CountingTestProcessor("test.idempotent", "IDEMPOTENT_EVENT");

        var messageId = await SeedMessage(tenantId, "IDEMPOTENT_EVENT", registry, handler);
        await RunProcessor("worker-1", registry, handler);
        await RunProcessor("worker-2", registry, handler);

        handler.ProcessCount.Should().Be(1);
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == messageId))
            .Status.Should().Be(OutboxMessageStatus.Completed);
    }

    [Fact]
    public async Task Expired_outbox_lock_can_be_reclaimed()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("LOCK_RECLAIM_EVENT", "test.reclaim");
        var handler = new CountingTestProcessor("test.reclaim", "LOCK_RECLAIM_EVENT");
        var messageId = await SeedMessage(tenantId, "LOCK_RECLAIM_EVENT", registry, handler);

        using (var scope = _fixture.CreateScope(services => ConfigureServices(services, registry, handler)))
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var message = await db.OutboxMessages.FirstAsync(m => m.Id == messageId);
            message.Status = OutboxMessageStatus.Processing;
            message.InitializedAtUtc = DateTimeOffset.UtcNow;
            message.ApplicableHandlerKeys = ["test.reclaim"];
            message.LockedBy = "stale-worker";
            message.LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
            message.NextAttemptAtUtc = null;
            message.AttemptCount = 1;
            db.EventHandlerDeliveries.Add(new EventHandlerDelivery
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                EventId = messageId,
                HandlerKey = "test.reclaim",
                Status = EventHandlerDeliveryStatus.Pending,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await RunProcessor("recovery-worker", registry, handler);

        handler.ProcessCount.Should().Be(1);
        using var verifyScope = _fixture.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await verifyDb.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == messageId))
            .Status.Should().Be(OutboxMessageStatus.Completed);
    }

    [Fact]
    public async Task Transient_handler_failure_is_retried()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("TRANSIENT_EVENT", "test.transient");
        var handler = new TransientThenSuccessProcessor("test.transient", "TRANSIENT_EVENT");
        TransientThenSuccessProcessor.Reset("test.transient");

        var messageId = await SeedMessage(tenantId, "TRANSIENT_EVENT", registry, handler);
        await RunProcessor("worker-1", registry, handler);

        using (var advanceScope = _fixture.CreateScope())
        {
            var advanceDb = advanceScope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var pendingDelivery = await advanceDb.EventHandlerDeliveries.FirstAsync(d => d.EventId == messageId);
            pendingDelivery.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await advanceDb.SaveChangesAsync();
        }

        await RunProcessor("worker-1", registry, handler);

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var delivery = await db.EventHandlerDeliveries.AsNoTracking().FirstAsync(d => d.EventId == messageId);
        delivery.AttemptCount.Should().BeGreaterThanOrEqualTo(1);
        TransientThenSuccessProcessor.GetAttempts("test.transient").Should().BeGreaterThanOrEqualTo(2);
        delivery.Status.Should().Be(EventHandlerDeliveryStatus.Processed);
        (await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == messageId))
            .Status.Should().Be(OutboxMessageStatus.Completed);
    }

    [Fact]
    public async Task Permanent_handler_failure_yields_completed_with_failures()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("PERM_FAIL_EVENT", "test.permanent");
        var handler = new PermanentFailureProcessor("test.permanent", "PERM_FAIL_EVENT");

        var messageId = await SeedMessage(tenantId, "PERM_FAIL_EVENT", registry, handler);
        await RunProcessor("worker-1", registry, handler);

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var message = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == messageId);
        message.Status.Should().Be(OutboxMessageStatus.CompletedWithFailures);
        (await db.EventHandlerDeliveries.AsNoTracking().FirstAsync(d => d.EventId == messageId))
            .Status.Should().Be(EventHandlerDeliveryStatus.FailedPermanent);
    }

    [Fact]
    public async Task Ignored_handler_failure_yields_completed_success()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("IGNORED_EVENT", "test.ignored");
        var handler = new IgnoredFailureProcessor("test.ignored", "IGNORED_EVENT");

        var messageId = await SeedMessage(tenantId, "IGNORED_EVENT", registry, handler);
        await RunProcessor("worker-1", registry, handler);

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var message = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == messageId);
        var delivery = await db.EventHandlerDeliveries.AsNoTracking().FirstAsync(d => d.EventId == messageId);

        message.Status.Should().Be(OutboxMessageStatus.Completed);
        delivery.Status.Should().Be(EventHandlerDeliveryStatus.ProcessedIgnored);
        delivery.LastErrorCode.Should().Be("handler.ignored");
    }

    [Fact]
    public async Task Tenant_scope_is_reconstructed_from_envelope()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("TENANT_SCOPE_EVENT", "test.tenant");

        TenantCapturingProcessor? handler = null;
        using var scope = _fixture.CreateScope(services =>
        {
            ConfigureServices(services, registry, null);
            services.RemoveAll<IIntegrationEventProcessor>();
            services.AddSingleton<IIntegrationEventProcessor>(sp =>
            {
                handler = new TenantCapturingProcessor(
                    sp.GetRequiredService<Binexus.Platform.Tenancy.ICurrentTenant>(),
                    "test.tenant",
                    "TENANT_SCOPE_EVENT");
                return handler;
            });
        });

        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.CreateVersion7();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = messageId,
            TenantId = tenantId,
            EventName = "TENANT_SCOPE_EVENT",
            PayloadJson = "{}",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Pending,
        });
        await db.SaveChangesAsync();

        await processor.ProcessBatchAsync("tenant-worker", CancellationToken.None);

        handler!.CapturedTenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task Cancellation_token_stops_worker_cleanly()
    {
        await _fixture.ResetOutboxAsync();
        var tenantId = Guid.CreateVersion7();
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers("SLOW_EVENT", "test.slow");
        var handler = new SlowProcessor("test.slow", "SLOW_EVENT", TimeSpan.FromMilliseconds(500));

        await Task.WhenAll(
            Enumerable.Range(0, 3)
                .Select(_ => SeedMessage(tenantId, "SLOW_EVENT", registry, handler))
                .ToArray());

        using var scope = _fixture.CreateScope(services => ConfigureServices(services, registry, handler));
        var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var completed = await Record.ExceptionAsync(
            () => processor.ProcessBatchAsync("cancel-worker", cts.Token));

        (completed is null || completed is OperationCanceledException or TaskCanceledException).Should().BeTrue();
    }

    private async Task<Guid> SeedMessage(
        Guid tenantId,
        string eventName,
        ConfigurableEventHandlerRegistry registry,
        IIntegrationEventProcessor? processor = null)
    {
        using var scope = _fixture.CreateScope(services => ConfigureServices(services, registry, processor));
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

    private async Task RunProcessor(
        string workerId,
        ConfigurableEventHandlerRegistry registry,
        IIntegrationEventProcessor? processor = null)
    {
        using var scope = _fixture.CreateScope(services => ConfigureServices(services, registry, processor));
        var outboxProcessor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        await outboxProcessor.ProcessBatchAsync(workerId, CancellationToken.None);
    }

    private static void ConfigureServices(
        IServiceCollection services,
        ConfigurableEventHandlerRegistry registry,
        IIntegrationEventProcessor? processor)
    {
        services.Replace(ServiceDescriptor.Singleton<IEventHandlerRegistry>(registry));
        if (processor is not null)
        {
            services.RemoveAll<IIntegrationEventProcessor>();
            services.AddSingleton<IIntegrationEventProcessor>(processor);
        }
    }
}

[CollectionDefinition("postgres")]
#pragma warning disable CA1711
public sealed class PostgresCollection : ICollectionFixture<PostgresTestFixture>;
#pragma warning restore CA1711
