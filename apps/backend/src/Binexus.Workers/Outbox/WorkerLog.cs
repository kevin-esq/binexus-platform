namespace Binexus.Workers.Outbox;

internal static partial class WorkerLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Outbox worker started. Poll interval: {PollInterval}")]
    public static partial void WorkerStarted(ILogger logger, TimeSpan pollInterval);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Outbox worker stopping due to cancellation")]
    public static partial void WorkerStopping(ILogger logger);
}
