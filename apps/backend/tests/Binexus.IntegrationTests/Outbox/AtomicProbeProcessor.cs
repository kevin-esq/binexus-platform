using Binexus.Platform.Messaging;

namespace Binexus.IntegrationTests.Outbox;

public sealed class AtomicProbeProcessor(
    string handlerKey,
    string eventName,
    bool shouldSucceed) : IIntegrationEventProcessor
{
    public int ProcessCount { get; private set; }

    public string HandlerKey { get; } = handlerKey;

    public string EventName { get; } = eventName;

    public Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ProcessCount++;
        message.PayloadJson = """{"probe":"seen"}""";

        if (!shouldSucceed)
        {
            throw new InvalidOperationException("atomic-failure");
        }

        return Task.FromResult(IntegrationProcessOutcome.Processed);
    }
}
