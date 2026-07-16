namespace Binexus.Platform.Branching.Persistence;

public sealed class BranchActivationChallenge
{
    private BranchActivationChallenge()
    {
    }

    public static BranchActivationChallenge Create(
        Guid id,
        Guid branchInstanceId,
        string publicKeyFingerprint,
        string installationTokenHash,
        string nonce,
        DateTimeOffset expiresAtUtc) =>
        new()
        {
            Id = id,
            BranchInstanceId = branchInstanceId,
            PublicKeyFingerprint = publicKeyFingerprint,
            InstallationTokenHash = installationTokenHash,
            Nonce = nonce,
            ExpiresAtUtc = expiresAtUtc,
        };

    public Guid Id { get; private set; }
    public Guid BranchInstanceId { get; private set; }
    public string PublicKeyFingerprint { get; private set; } = string.Empty;
    public string InstallationTokenHash { get; private set; } = string.Empty;
    public string Nonce { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public void MarkConsumed(DateTimeOffset consumedAtUtc)
    {
        if (ConsumedAtUtc is not null)
        {
            throw new InvalidOperationException("Challenge already consumed.");
        }

        ConsumedAtUtc = consumedAtUtc;
    }
}
