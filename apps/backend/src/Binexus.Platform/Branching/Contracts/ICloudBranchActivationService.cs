using Binexus.Platform.Branching.Activation;

namespace Binexus.Platform.Branching.Contracts;

public interface ICloudBranchActivationService
{
    Task<GenerateBranchActivationResult> GenerateAsync(
        Guid tenantId,
        Guid userId,
        Guid branchId,
        CancellationToken cancellationToken = default);

    Task<CreateBranchActivationChallengeResult> CreateChallengeAsync(
        Guid branchInstanceId,
        string publicKey,
        string installationTokenHash,
        CancellationToken cancellationToken = default);

    Task<ExchangeBranchActivationResult> ExchangeAsync(
        string activationCode,
        Guid branchInstanceId,
        string publicKey,
        Guid challengeId,
        string signature,
        string installationTokenHash,
        CancellationToken cancellationToken = default);

    Task<ResumeBranchActivationResult> ResumeAsync(
        Guid activationId,
        Guid branchInstanceId,
        string publicKey,
        Guid challengeId,
        string signature,
        string installationTokenHash,
        CancellationToken cancellationToken = default);

    Task<ConfirmBranchActivationResult> ConfirmAsync(
        Guid activationId,
        string receipt,
        string installationToken,
        CancellationToken cancellationToken = default);
}
