namespace Binexus.Platform.Messaging;

public static class EventHandlerRegistryValidator
{
    public static IReadOnlyList<string> NormalizeHandlerKeys(IEnumerable<string> handlerKeys)
    {
        var ordered = handlerKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        var duplicates = ordered
            .GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate handler keys for the same event: {string.Join(", ", duplicates)}");
        }

        return ordered;
    }

    public static void ValidateProcessorKeys(IEnumerable<IIntegrationEventProcessor> processors)
    {
        var duplicates = processors
            .GroupBy(processor => processor.HandlerKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate IIntegrationEventProcessor.HandlerKey registrations: {string.Join(", ", duplicates)}");
        }
    }
}
