namespace Binexus.Platform.Messaging;

/// <summary>
/// Registry of integration event handlers. Used to snapshot applicable handlers at first claim.
/// </summary>
public interface IEventHandlerRegistry
{
    IReadOnlyList<string> GetHandlersForEvent(string eventName);
}

public interface IIntegrationEventProcessor
{
    string HandlerKey { get; }

    string EventName { get; }

    Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public enum IntegrationProcessOutcome
{
    Processed,
    ProcessedIgnored,
}
