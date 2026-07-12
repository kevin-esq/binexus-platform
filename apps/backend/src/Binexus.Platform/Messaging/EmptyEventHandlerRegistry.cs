namespace Binexus.Platform.Messaging;

/// <summary>Empty registry for Gate 2A. Handlers register during module migration stages.</summary>
public sealed class EmptyEventHandlerRegistry : IEventHandlerRegistry
{
    public IReadOnlyList<string> GetHandlersForEvent(string eventName) => [];
}
