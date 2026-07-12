using System.Collections.Concurrent;
using Binexus.Platform.Messaging;
using Binexus.Platform.Tenancy;

namespace Binexus.IntegrationTests.Outbox;

public sealed class ConfigurableEventHandlerRegistry : IEventHandlerRegistry
{
    private readonly Dictionary<string, IReadOnlyList<string>> _handlers = new(StringComparer.Ordinal);

    public void SetHandlers(string eventName, params string[] handlerKeys) =>
        _handlers[eventName] = EventHandlerRegistryValidator.NormalizeHandlerKeys(handlerKeys);

    public IReadOnlyList<string> GetHandlersForEvent(string eventName) =>
        _handlers.TryGetValue(eventName, out var keys) ? keys : [];
}

public sealed class CountingTestProcessor(string handlerKey, string eventName, Action? onProcess = null)
    : IIntegrationEventProcessor
{
    private int _processCount;

    public int ProcessCount => Volatile.Read(ref _processCount);

    public string HandlerKey { get; } = handlerKey;

    public string EventName { get; } = eventName;

    public Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _processCount);
        onProcess?.Invoke();
        return Task.FromResult(IntegrationProcessOutcome.Processed);
    }
}

public sealed class TransientThenSuccessProcessor(string handlerKey, string eventName) : IIntegrationEventProcessor
{
    private static readonly ConcurrentDictionary<string, int> Attempts = new(StringComparer.Ordinal);

    public string HandlerKey { get; } = handlerKey;

    public string EventName { get; } = eventName;

    public Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var attempt = Attempts.AddOrUpdate(HandlerKey, 1, static (_, current) => current + 1);
        if (attempt == 1)
        {
            throw new InvalidOperationException("transient");
        }

        return Task.FromResult(IntegrationProcessOutcome.Processed);
    }

    public static void Reset(string handlerKey) => Attempts.TryRemove(handlerKey, out _);

    public static int GetAttempts(string handlerKey) =>
        Attempts.TryGetValue(handlerKey, out var count) ? count : 0;
}

public sealed class PermanentFailureProcessor(string handlerKey, string eventName) : IIntegrationEventProcessor
{
    public string HandlerKey { get; } = handlerKey;

    public string EventName { get; } = eventName;

    public Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        throw new PermanentHandlerException("handler.permanent", "permanent-business-failure");
}

public sealed class IgnoredFailureProcessor(string handlerKey, string eventName) : IIntegrationEventProcessor
{
    public string HandlerKey { get; } = handlerKey;

    public string EventName { get; } = eventName;

    public Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        throw new IgnoredHandlerException("handler.ignored", "ignored-business-case");
}

public sealed class TenantCapturingProcessor(
    ICurrentTenant currentTenant,
    string handlerKey,
    string eventName) : IIntegrationEventProcessor
{
    public Guid? CapturedTenantId { get; private set; }

    public string HandlerKey { get; } = handlerKey;

    public string EventName { get; } = eventName;

    public Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        CapturedTenantId = currentTenant.Current?.TenantId;
        return Task.FromResult(IntegrationProcessOutcome.Processed);
    }
}

public sealed class SlowProcessor(string handlerKey, string eventName, TimeSpan delay) : IIntegrationEventProcessor
{
    public string HandlerKey { get; } = handlerKey;

    public string EventName { get; } = eventName;

    public async Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);
        return IntegrationProcessOutcome.Processed;
    }
}
