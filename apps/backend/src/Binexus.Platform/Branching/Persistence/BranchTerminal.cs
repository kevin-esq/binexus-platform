namespace Binexus.Platform.Branching.Persistence;

/// <summary>
/// Operational workstation ("Caja 1"). Minted by the Branch on approval with a UUIDv7 that becomes
/// the canonical <c>TerminalId</c> for Sales in a future PR. Policy in PR 4: one Device → one Terminal.
/// </summary>
public sealed class BranchTerminal
{
    public const string PendingConfirmationStatus = "PendingConfirmation";
    public const string ActiveStatus = "Active";
    public const string DisabledStatus = "Disabled";

    private BranchTerminal()
    {
    }

    public static BranchTerminal CreatePendingConfirmation(
        Guid terminalId,
        Guid branchInstanceId,
        Guid deviceId,
        string name,
        string normalizedName,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = terminalId,
            BranchInstanceId = branchInstanceId,
            DeviceId = deviceId,
            Name = name,
            NormalizedName = normalizedName,
            Status = PendingConfirmationStatus,
            CreatedAtUtc = createdAtUtc,
        };

    public Guid Id { get; private set; }
    public Guid BranchInstanceId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string Status { get; private set; } = PendingConfirmationStatus;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ActivatedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public void MarkActive(DateTimeOffset activatedAtUtc)
    {
        Status = ActiveStatus;
        ActivatedAtUtc = activatedAtUtc;
    }

    public void Disable() => Status = DisabledStatus;
}
