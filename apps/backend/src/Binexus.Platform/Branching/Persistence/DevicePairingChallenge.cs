namespace Binexus.Platform.Branching.Persistence;

/// <summary>
/// Single-use proof-of-possession challenge. The <see cref="Phase"/> discriminates the exchange
/// challenge (bound to a session) from the post-approval confirmation challenge (bound to a request
/// and the minted terminal + receipt hash).
/// </summary>
public sealed class DevicePairingChallenge
{
    public const string ExchangePhase = "Exchange";
    public const string ConfirmationPhase = "Confirmation";
    public const string ReceiptReissuePhase = "ReceiptReissue";

    private DevicePairingChallenge()
    {
    }

    public static DevicePairingChallenge CreateExchange(
        Guid id,
        Guid branchInstanceId,
        Guid pairingSessionId,
        Guid deviceId,
        string publicKeyFingerprint,
        string credentialHash,
        string nonce,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = id,
            Phase = ExchangePhase,
            BranchInstanceId = branchInstanceId,
            PairingSessionId = pairingSessionId,
            DeviceId = deviceId,
            PublicKeyFingerprint = publicKeyFingerprint,
            CredentialHash = credentialHash,
            Nonce = nonce,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = createdAtUtc,
        };

    public static DevicePairingChallenge CreateReceiptReissue(
        Guid id,
        Guid branchInstanceId,
        Guid pairingRequestId,
        Guid deviceId,
        string publicKeyFingerprint,
        string credentialHash,
        string nonce,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = id,
            Phase = ReceiptReissuePhase,
            BranchInstanceId = branchInstanceId,
            PairingRequestId = pairingRequestId,
            DeviceId = deviceId,
            PublicKeyFingerprint = publicKeyFingerprint,
            CredentialHash = credentialHash,
            Nonce = nonce,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = createdAtUtc,
        };

    public static DevicePairingChallenge CreateConfirmation(
        Guid id,
        Guid branchInstanceId,
        Guid pairingRequestId,
        Guid deviceId,
        Guid terminalId,
        string publicKeyFingerprint,
        string credentialHash,
        string pairingReceiptHash,
        string nonce,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = id,
            Phase = ConfirmationPhase,
            BranchInstanceId = branchInstanceId,
            PairingRequestId = pairingRequestId,
            DeviceId = deviceId,
            TerminalId = terminalId,
            PublicKeyFingerprint = publicKeyFingerprint,
            CredentialHash = credentialHash,
            PairingReceiptHash = pairingReceiptHash,
            Nonce = nonce,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = createdAtUtc,
        };

    public Guid Id { get; private set; }
    public string Phase { get; private set; } = ExchangePhase;
    public Guid BranchInstanceId { get; private set; }
    public Guid? PairingSessionId { get; private set; }
    public Guid? PairingRequestId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid? TerminalId { get; private set; }
    public string PublicKeyFingerprint { get; private set; } = string.Empty;
    public string CredentialHash { get; private set; } = string.Empty;
    public string? PairingReceiptHash { get; private set; }
    public string Nonce { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
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
