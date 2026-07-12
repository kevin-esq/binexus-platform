namespace Binexus.Platform.Messaging;

public sealed class IgnoredHandlerException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
