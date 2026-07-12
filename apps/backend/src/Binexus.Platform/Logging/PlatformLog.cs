using Microsoft.Extensions.Logging;

namespace Binexus.Platform.Logging;

internal static partial class PlatformLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Transactional command {CommandName} failed")]
    public static partial void TransactionalCommandFailed(ILogger logger, Exception exception, string commandName);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    public static partial void UnhandledException(ILogger logger, Exception exception, string method, string path);
}
