using System.ComponentModel.DataAnnotations;

namespace Binexus.Platform.Branching.Configuration;

/// <summary>
/// Branch-only device pairing configuration. The pepper is required and must not be a placeholder
/// outside Development. It is distinct from <see cref="CloudActivationOptions.CodePepper"/> and is
/// never stored in the database or the repository.
/// </summary>
public sealed class DevicePairingOptions
{
    public const string SectionName = "BranchPairing";
    public const string KnownDevelopmentPepper = "development-only-branch-pairing-pepper-2026";

    [Required]
    [MinLength(32)]
    public string CodePepper { get; init; } = string.Empty;

    public TimeSpan CodeTtl { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan RequestTtl { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan ExchangeChallengeTtl { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan ConfirmationChallengeTtl { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan StatusTokenTtl { get; init; } = TimeSpan.FromMinutes(15);

    [Range(1, 100)]
    public int MaxFailedAttempts { get; init; } = 5;

    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(15);

    [Range(1, 1000)]
    public int AdminPermitLimit { get; init; } = 10;

    [Range(1, 1000)]
    public int MachinePermitLimit { get; init; } = 30;
}
