using Binexus.Platform.Messaging;
using FluentAssertions;

namespace Binexus.UnitTests.Messaging;

public sealed class EventHandlerRegistryValidatorTests
{
    [Fact]
    public void NormalizeHandlerKeys_sorts_and_rejects_duplicates()
    {
        var keys = EventHandlerRegistryValidator.NormalizeHandlerKeys(["z.handler", "a.handler", "m.handler"]);
        keys.Should().Equal("a.handler", "m.handler", "z.handler");
    }

    [Fact]
    public void NormalizeHandlerKeys_throws_for_duplicate_keys()
    {
        var act = () => EventHandlerRegistryValidator.NormalizeHandlerKeys(["dup", "dup"]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate handler keys*");
    }

    [Fact]
    public void ValidateProcessorKeys_throws_for_duplicate_registrations()
    {
        IIntegrationEventProcessor[] processors =
        [
            new StubProcessor("same.key", "A"),
            new StubProcessor("same.key", "B"),
        ];

        var act = () => EventHandlerRegistryValidator.ValidateProcessorKeys(processors);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate IIntegrationEventProcessor*");
    }

    private sealed class StubProcessor(string handlerKey, string eventName) : IIntegrationEventProcessor
    {
        public string HandlerKey { get; } = handlerKey;

        public string EventName { get; } = eventName;

        public Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(IntegrationProcessOutcome.Processed);
    }
}
