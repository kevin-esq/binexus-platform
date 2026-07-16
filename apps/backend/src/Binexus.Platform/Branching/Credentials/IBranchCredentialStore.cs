namespace Binexus.Platform.Branching.Credentials;

public enum BranchActivationStage
{
    NotStarted = 0,
    MaterialPrepared = 1,
    Reserved = 2,
    CloudConfirmed = 3,
    FinalizeRequired = 4,
    Completed = 5,
}

public sealed record PermanentBranchCredentials(
    Guid BranchInstanceId,
    Guid TenantId,
    Guid BranchId,
    Guid ActivationId,
    string PublicKey,
    string PublicKeyFingerprint,
    string InstallationToken,
    string InstallationTokenHash,
    string PrivateKeyPkcs8Base64Url,
    DateTimeOffset ActivatedAtUtc);

/// <summary>
/// Local activation progress. Never stores the raw activation code.
/// </summary>
public sealed record BranchActivationSession(
    Guid LocalAttemptId,
    BranchActivationStage Stage,
    Guid BranchInstanceId,
    string PublicKey,
    string PublicKeyFingerprint,
    string InstallationTokenHash,
    string? PrivateKeyPkcs8Base64Url,
    Guid? ChallengeId,
    string? Nonce,
    Guid? ActivationId,
    Guid? TenantId,
    Guid? BranchId,
    string? Receipt,
    string? InstallationToken,
    DateTimeOffset UpdatedAtUtc);

public interface IBranchCredentialStore
{
    Task<BranchActivationSession?> GetSessionAsync(CancellationToken cancellationToken = default);

    Task SaveSessionAsync(BranchActivationSession session, CancellationToken cancellationToken = default);

    Task ClearSessionAsync(CancellationToken cancellationToken = default);

    Task<PermanentBranchCredentials?> GetPermanentAsync(CancellationToken cancellationToken = default);

    Task SavePermanentAsync(PermanentBranchCredentials credentials, CancellationToken cancellationToken = default);
}
