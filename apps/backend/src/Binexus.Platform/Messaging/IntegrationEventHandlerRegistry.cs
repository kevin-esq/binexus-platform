using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Platform.Messaging;

public sealed class IntegrationEventHandlerRegistry(IServiceProvider serviceProvider) : IEventHandlerRegistry
{
    public IReadOnlyList<string> GetHandlersForEvent(string eventName)
    {
        using var scope = serviceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventProcessor>()
            .Where(processor => string.Equals(processor.EventName, eventName, StringComparison.Ordinal))
            .Select(processor => processor.HandlerKey);

        return EventHandlerRegistryValidator.NormalizeHandlerKeys(handlers);
    }
}
