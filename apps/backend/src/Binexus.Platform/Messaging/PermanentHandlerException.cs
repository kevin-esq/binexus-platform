namespace Binexus.Platform.Messaging;

/// <summary>Thrown by handlers for non-recoverable business failures.</summary>
public sealed class PermanentHandlerException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
