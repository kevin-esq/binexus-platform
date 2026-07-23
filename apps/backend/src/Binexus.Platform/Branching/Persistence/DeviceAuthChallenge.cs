namespace Binexus.Platform.Branching.Persistence;

/// <summary>Single-use DAT issuance challenge. Status Open → Consumed must be atomic.</summary>
public sealed class DeviceAuthChallenge
{
    public const string OpenStatus = "Open";
    public const string ConsumedStatus = "Consumed";

    private DeviceAuthChallenge()
    {
    }

    public static DeviceAuthChallenge Create(
        Guid id,
        Guid branchInstanceId,
        Guid deviceId,
        string nonce,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = id,
            BranchInstanceId = branchInstanceId,
            DeviceId = deviceId,
            Nonce = nonce,
            Status = OpenStatus,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = createdAtUtc,
        };

    public Guid Id { get; private set; }
    public Guid BranchInstanceId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string Nonce { get; private set; } = string.Empty;
    public string Status { get; private set; } = OpenStatus;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public uint Version { get; private set; }
}
