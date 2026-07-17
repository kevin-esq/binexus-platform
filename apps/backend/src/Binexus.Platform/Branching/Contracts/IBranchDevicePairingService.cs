using Binexus.Platform.Branching.Pairing;

namespace Binexus.Platform.Branching.Contracts;

/// <summary>
/// Anonymous machine ceremony surface (Device-driven). Every operation is protected by the pairing
/// code, single-use challenges, ECDSA proof-of-possession, TTLs, persisted lockout and rate limits.
/// </summary>
public interface IBranchDevicePairingService
{
    Task<CreateExchangeChallengeResult> CreateExchangeChallengeAsync(
        Guid pairingSessionId,
        string pairingCode,
        Guid deviceId,
        string publicKey,
        string credentialHash,
        CancellationToken cancellationToken = default);

    Task<PairingExchangeResult> ExchangeAsync(
        Guid pairingSessionId,
        string pairingCode,
        Guid deviceId,
        string publicKey,
        Guid challengeId,
        string signature,
        string credentialHash,
        string terminalName,
        CancellationToken cancellationToken = default);

    Task<PairingStatusResult> GetStatusAsync(
        Guid pairingRequestId,
        string pairingStatusToken,
        CancellationToken cancellationToken = default);

    Task<CreateReceiptReissueChallengeResult> CreateReceiptReissueChallengeAsync(
        Guid pairingRequestId,
        string pairingStatusToken,
        CancellationToken cancellationToken = default);

    Task<ReissuePairingReceiptResult> ReissueReceiptAsync(
        Guid pairingRequestId,
        string pairingStatusToken,
        Guid reissueChallengeId,
        string signature,
        CancellationToken cancellationToken = default);

    Task<PairingConfirmResult> ConfirmAsync(
        Guid pairingRequestId,
        Guid confirmationChallengeId,
        string signature,
        string pairingReceipt,
        string pairingStatusToken,
        CancellationToken cancellationToken = default);
}
