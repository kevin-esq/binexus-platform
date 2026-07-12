using Binexus.SharedKernel.Abstractions;

namespace Binexus.Platform.Messaging;

/// <summary>
/// Domain event envelope stored in the outbox table.
/// Handler progress lives exclusively in <see cref="EventHandlerDelivery"/>.
/// </summary>
public sealed class OutboxMessage : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string EventName { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;

    /// <summary>
    /// Handler keys snapshotted when the message is first claimed for delivery.
    /// Immutable after snapshot — new handlers in future releases do not apply retroactively.
    /// </summary>
    public string[]? ApplicableHandlerKeys { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }

    public string? LockedBy { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public string? CorrelationId { get; set; }

    public string? CausationId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? InitializedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public ICollection<EventHandlerDelivery> Deliveries { get; set; } = new List<EventHandlerDelivery>();
}
