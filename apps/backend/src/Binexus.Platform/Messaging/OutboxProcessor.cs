using Binexus.Platform.Configuration;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.Messaging;

public interface IOutboxProcessor
{
    Task<int> ProcessBatchAsync(string workerId, CancellationToken cancellationToken);
}

/// <summary>
/// Claims and dispatches outbox messages with per-handler inbox tracking.
/// Lock expiry in SQL uses PostgreSQL <c>NOW()</c> as the authoritative clock.
/// Application <see cref="TimeProvider"/> is used for scheduling/backoff timestamps written to rows.
/// </summary>
public sealed class OutboxProcessor(
    BinexusDbContext dbContext,
    IEventHandlerRegistry handlerRegistry,
    IEnumerable<IIntegrationEventProcessor> processors,
    ICurrentTenant currentTenant,
    TimeProvider timeProvider,
    IOptions<OutboxWorkerOptions> options,
    ILogger<OutboxProcessor> logger) : IOutboxProcessor
{
    private readonly Dictionary<string, IIntegrationEventProcessor> _processorsByKey =
        CreateProcessorMap(processors);

    private static Dictionary<string, IIntegrationEventProcessor> CreateProcessorMap(
        IEnumerable<IIntegrationEventProcessor> processors)
    {
        var processorList = processors.ToArray();
        EventHandlerRegistryValidator.ValidateProcessorKeys(processorList);
        return processorList.ToDictionary(p => p.HandlerKey, StringComparer.Ordinal);
    }

    public async Task<int> ProcessBatchAsync(string workerId, CancellationToken cancellationToken)
    {
        var processed = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var claimed = await TryClaimNextMessageAsync(workerId, cancellationToken);
            if (claimed is null)
            {
                break;
            }

            currentTenant.SetContext(new TenantContext(
                claimed.TenantId,
                UserId: null,
                Role: null,
                BranchId: null,
                RequestId: claimed.Id.ToString()));

            try
            {
                var batchProcessed = await ProcessClaimedMessageAsync(claimed, workerId, cancellationToken);
                processed += batchProcessed;
                if (batchProcessed == 0 && claimed.Status == OutboxMessageStatus.Processing)
                {
                    break;
                }
            }
            finally
            {
                currentTenant.Clear();
            }
        }

        return processed;
    }

    private Task<OutboxMessage?> TryClaimNextMessageAsync(string workerId, CancellationToken cancellationToken) =>
        dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            var lockUntil = now.Add(options.Value.LockDuration);

            var messageId = await dbContext.Database
                .SqlQuery<Guid>($"""
                    SELECT id AS "Value"
                    FROM outbox_messages
                    WHERE status IN ('Pending', 'Processing', 'FailedTransient')
                      AND (locked_until_utc IS NULL OR locked_until_utc < NOW())
                      AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= NOW())
                    ORDER BY occurred_at_utc
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(cancellationToken);

            if (messageId == Guid.Empty)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var message = await dbContext.OutboxMessages
                .IgnoreQueryFilters()
                .Include(m => m.Deliveries)
                .FirstAsync(m => m.Id == messageId, cancellationToken);

            if (message.InitializedAtUtc is null)
            {
                var handlerKeys = EventHandlerRegistryValidator
                    .NormalizeHandlerKeys(handlerRegistry.GetHandlersForEvent(message.EventName))
                    .ToArray();
                message.ApplicableHandlerKeys = handlerKeys;
                message.InitializedAtUtc = now;

                if (handlerKeys.Length == 0)
                {
                    message.Status = OutboxMessageStatus.Completed;
                    message.CompletedAtUtc = now;
                    message.LockedUntilUtc = null;
                    message.LockedBy = null;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return message;
                }

                foreach (var handlerKey in handlerKeys)
                {
                    dbContext.EventHandlerDeliveries.Add(new EventHandlerDelivery
                    {
                        Id = Guid.CreateVersion7(now),
                        TenantId = message.TenantId,
                        EventId = message.Id,
                        HandlerKey = handlerKey,
                        Status = EventHandlerDeliveryStatus.Pending,
                        CreatedAtUtc = now,
                    });
                }
            }

            message.Status = OutboxMessageStatus.Processing;
            message.LockedUntilUtc = lockUntil;
            message.LockedBy = workerId;
            message.AttemptCount += 1;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return message;
        });

    private async Task<int> ProcessClaimedMessageAsync(
        OutboxMessage message,
        string workerId,
        CancellationToken cancellationToken)
    {
        if (message.Status is OutboxMessageStatus.Completed or OutboxMessageStatus.CompletedWithFailures)
        {
            return 0;
        }

        var processed = 0;
        var deliveryIds = await dbContext.EventHandlerDeliveries
            .IgnoreQueryFilters()
            .Where(d => d.EventId == message.Id)
            .OrderBy(d => d.HandlerKey)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        foreach (var deliveryId in deliveryIds)
        {
            processed += await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                var now = timeProvider.GetUtcNow();
                var lockUntil = now.Add(options.Value.LockDuration);

                await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

                var claimableId = await dbContext.Database
                    .SqlQuery<Guid>($"""
                        SELECT id AS "Value"
                        FROM event_handler_deliveries
                        WHERE id = {deliveryId}
                          AND status NOT IN ('Processed', 'ProcessedIgnored', 'FailedPermanent')
                          AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= NOW())
                          AND (locked_until_utc IS NULL OR locked_until_utc < NOW())
                        FOR UPDATE
                        """)
                    .FirstOrDefaultAsync(cancellationToken);

                if (claimableId == Guid.Empty)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return 0;
                }

                var locked = await dbContext.EventHandlerDeliveries
                    .IgnoreQueryFilters()
                    .FirstAsync(d => d.Id == deliveryId, cancellationToken);

                locked.Status = EventHandlerDeliveryStatus.Processing;
                locked.LockedBy = workerId;
                locked.LockedUntilUtc = lockUntil;
                locked.AttemptCount += 1;

                await dbContext.Database.ExecuteSqlRawAsync(
                    "SAVEPOINT handler_effects",
                    cancellationToken);

                var processedIncrement = 0;
                try
                {
                    if (!_processorsByKey.TryGetValue(locked.HandlerKey, out var processor))
                    {
                        await dbContext.Database.ExecuteSqlRawAsync(
                            "ROLLBACK TO SAVEPOINT handler_effects",
                            cancellationToken);
                        dbContext.ChangeTracker.Clear();
                        locked = await dbContext.EventHandlerDeliveries
                            .IgnoreQueryFilters()
                            .FirstAsync(d => d.Id == deliveryId, cancellationToken);
                        locked.Status = EventHandlerDeliveryStatus.FailedPermanent;
                        locked.LastErrorCode = "handler.not_registered";
                        locked.LastErrorMessage = "Handler not registered at processing time.";
                    }
                    else
                    {
                        var freshMessage = await dbContext.OutboxMessages
                            .IgnoreQueryFilters()
                            .FirstAsync(m => m.Id == message.Id, cancellationToken);
                        var outcome = await processor.ProcessAsync(freshMessage, cancellationToken);
                        locked.Status = ToDeliveryStatus(outcome);
                        locked.ProcessedAtUtc = timeProvider.GetUtcNow();
                        locked.LastErrorCode = null;
                        locked.LastErrorMessage = null;
                        processedIncrement = 1;
                    }
                }
                catch (IgnoredHandlerException ex)
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "ROLLBACK TO SAVEPOINT handler_effects",
                        cancellationToken);
                    dbContext.ChangeTracker.Clear();
                    locked = await dbContext.EventHandlerDeliveries
                        .IgnoreQueryFilters()
                        .FirstAsync(d => d.Id == deliveryId, cancellationToken);
                    locked.Status = EventHandlerDeliveryStatus.ProcessedIgnored;
                    locked.ProcessedAtUtc = timeProvider.GetUtcNow();
                    locked.LastErrorCode = ex.Code;
                    locked.LastErrorMessage = Sanitize(ex.Message);
                    processedIncrement = 1;
                }
                catch (PermanentHandlerException ex)
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "ROLLBACK TO SAVEPOINT handler_effects",
                        cancellationToken);
                    dbContext.ChangeTracker.Clear();
                    locked = await dbContext.EventHandlerDeliveries
                        .IgnoreQueryFilters()
                        .FirstAsync(d => d.Id == deliveryId, cancellationToken);
                    locked.Status = EventHandlerDeliveryStatus.FailedPermanent;
                    locked.LastErrorCode = ex.Code;
                    locked.LastErrorMessage = Sanitize(ex.Message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "ROLLBACK TO SAVEPOINT handler_effects",
                        cancellationToken);
                    dbContext.ChangeTracker.Clear();
                    locked = await dbContext.EventHandlerDeliveries
                        .IgnoreQueryFilters()
                        .FirstAsync(d => d.Id == deliveryId, cancellationToken);
                    locked.Status = EventHandlerDeliveryStatus.FailedTransient;
                    locked.LastErrorCode = "handler.transient";
                    locked.LastErrorMessage = Sanitize(ex.Message);
                    locked.NextAttemptAtUtc = timeProvider.GetUtcNow().Add(Backoff(locked.AttemptCount));
                    OutboxProcessorLog.HandlerTransientFailure(logger, locked.HandlerKey, message.Id);
                }

                locked.LockedUntilUtc = null;
                locked.LockedBy = null;
                await FinalizeOutboxStatusAsync(message.Id, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return processedIncrement;
            });
        }

        await ReleaseOutboxClaimAsync(message.Id, cancellationToken);
        return processed;
    }

    private async Task ReleaseOutboxClaimAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await dbContext.OutboxMessages
            .IgnoreQueryFilters()
            .FirstAsync(m => m.Id == messageId, cancellationToken);
        if (message.Status is OutboxMessageStatus.Completed or OutboxMessageStatus.CompletedWithFailures)
        {
            return;
        }

        message.LockedUntilUtc = null;
        message.LockedBy = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task FinalizeOutboxStatusAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var deliveries = await dbContext.EventHandlerDeliveries
            .IgnoreQueryFilters()
            .Where(d => d.EventId == messageId)
            .ToListAsync(cancellationToken);

        var message = await dbContext.OutboxMessages
            .IgnoreQueryFilters()
            .FirstAsync(m => m.Id == messageId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (deliveries.Count == 0)
        {
            message.Status = OutboxMessageStatus.Completed;
            message.CompletedAtUtc = now;
            return;
        }

        if (deliveries.Any(d => d.Status is EventHandlerDeliveryStatus.Pending
                or EventHandlerDeliveryStatus.Processing
                or EventHandlerDeliveryStatus.FailedTransient))
        {
            message.Status = OutboxMessageStatus.Processing;
            return;
        }

        if (deliveries.All(IsTerminalSuccess))
        {
            message.Status = OutboxMessageStatus.Completed;
            message.CompletedAtUtc = now;
            message.LockedUntilUtc = null;
            message.LockedBy = null;
            return;
        }

        if (deliveries.Any(d => d.Status == EventHandlerDeliveryStatus.FailedPermanent))
        {
            message.Status = OutboxMessageStatus.CompletedWithFailures;
            message.CompletedAtUtc = now;
            message.LockedUntilUtc = null;
            message.LockedBy = null;
        }
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));

    private static EventHandlerDeliveryStatus ToDeliveryStatus(IntegrationProcessOutcome outcome) => outcome switch
    {
        IntegrationProcessOutcome.Processed => EventHandlerDeliveryStatus.Processed,
        IntegrationProcessOutcome.ProcessedIgnored => EventHandlerDeliveryStatus.ProcessedIgnored,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static bool IsTerminalSuccess(EventHandlerDelivery delivery) =>
        delivery.Status is EventHandlerDeliveryStatus.Processed or EventHandlerDeliveryStatus.ProcessedIgnored;

    private static string Sanitize(string message) =>
        message.Length <= 512 ? message : message[..512];
}
