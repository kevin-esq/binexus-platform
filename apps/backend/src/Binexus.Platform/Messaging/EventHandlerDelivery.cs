using Binexus.SharedKernel.Abstractions;

namespace Binexus.Platform.Messaging;

/// <summary>
/// Idempotent handler execution record. Unique key: (TenantId, EventId, HandlerKey).
/// </summary>
public sealed class EventHandlerDelivery : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid EventId { get; set; }

    public string HandlerKey { get; set; } = string.Empty;

    public EventHandlerDeliveryStatus Status { get; set; } = EventHandlerDeliveryStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }

    public string? LockedBy { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public OutboxMessage OutboxMessage { get; set; } = null!;
}
