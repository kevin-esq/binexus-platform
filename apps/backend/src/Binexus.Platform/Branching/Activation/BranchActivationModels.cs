namespace Binexus.Platform.Branching.Activation;

public static class BranchActivationErrorCodes
{
    public const string ActivationInvalid = "ACTIVATION_INVALID";
    public const string BranchAlreadyActive = "BRANCH_ALREADY_ACTIVE";
    public const string ActivationInProgress = "ACTIVATION_IN_PROGRESS";
    public const string BranchNotFound = "BRANCH_NOT_FOUND";
    public const string Forbidden = "FORBIDDEN";
}

public sealed class BranchActivationException : Exception
{
    public BranchActivationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed record GenerateBranchActivationResult(
    Guid ActivationId,
    string ActivationCode,
    DateTimeOffset ExpiresAtUtc);

public sealed record CreateBranchActivationChallengeResult(
    Guid ChallengeId,
    string Nonce,
    DateTimeOffset ExpiresAtUtc,
    string PublicKeyFingerprint);

public sealed record ExchangeBranchActivationResult(
    Guid ActivationId,
    Guid TenantId,
    Guid BranchId,
    string Receipt,
    DateTimeOffset ReservedUntilUtc);

public sealed record ResumeBranchActivationResult(
    Guid ActivationId,
    Guid TenantId,
    Guid BranchId,
    string Receipt,
    DateTimeOffset ReservedUntilUtc);

public sealed record ConfirmBranchActivationResult(
    Guid ActivationId,
    Guid TenantId,
    Guid BranchId,
    Guid BranchInstanceId,
    DateTimeOffset ActivatedAtUtc,
    bool AlreadyActive);
