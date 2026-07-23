namespace Binexus.Platform.Branching.DeviceAuth;

public interface ICurrentDevice
{
    Guid? DeviceId { get; }
    string? SecurityStamp { get; }
    void SetContext(Guid deviceId, string securityStamp);
    void Clear();
}

public interface ICurrentTerminal
{
    Guid? TerminalId { get; }
    void SetContext(Guid terminalId);
    void Clear();
}

public sealed class CurrentDevice : ICurrentDevice
{
    private static readonly AsyncLocal<Holder?> Current = new();

    public Guid? DeviceId => Current.Value?.DeviceId;
    public string? SecurityStamp => Current.Value?.SecurityStamp;

    public void SetContext(Guid deviceId, string securityStamp) =>
        Current.Value = new Holder(deviceId, securityStamp);

    public void Clear() => Current.Value = null;

    private sealed record Holder(Guid DeviceId, string SecurityStamp);
}

public sealed class CurrentTerminal : ICurrentTerminal
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public Guid? TerminalId => Current.Value;

    public void SetContext(Guid terminalId) => Current.Value = terminalId;

    public void Clear() => Current.Value = null;
}
