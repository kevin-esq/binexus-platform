namespace Binexus.Platform.Messaging;

/// <summary>
/// Lifecycle of an outbox message (domain event envelope persisted for async dispatch).
/// </summary>
public enum OutboxMessageStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    CompletedWithFailures = 3,
    FailedTransient = 4,
    FailedPermanent = 5,
}
