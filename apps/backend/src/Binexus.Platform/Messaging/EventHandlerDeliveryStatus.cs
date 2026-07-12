namespace Binexus.Platform.Messaging;

/// <summary>Per-handler delivery state. Source of truth for handler progress.</summary>
public enum EventHandlerDeliveryStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    FailedTransient = 3,
    FailedPermanent = 4,
    ProcessedIgnored = 5,
}
