using Microsoft.Extensions.Logging;

namespace Binexus.Platform.Messaging;

internal static partial class OutboxProcessorLog
{
    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Handler {HandlerKey} transient failure for event {EventId}")]
    public static partial void HandlerTransientFailure(ILogger logger, string handlerKey, Guid eventId);
}
