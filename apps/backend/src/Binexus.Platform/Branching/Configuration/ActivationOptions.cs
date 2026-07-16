using System.ComponentModel.DataAnnotations;

namespace Binexus.Platform.Branching.Configuration;

public sealed class CloudActivationOptions
{
    public const string SectionName = "CloudActivation";
    public const string KnownDevelopmentPepper = "development-only-branch-activation-pepper-2026";

    [Required]
    [MinLength(32)]
    public string CodePepper { get; init; } = string.Empty;

    public TimeSpan CodeTtl { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan ReservedDuration { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan ChallengeTtl { get; init; } = TimeSpan.FromMinutes(2);

    [Range(1, 100)]
    public int MaxFailedAttempts { get; init; } = 5;

    [Range(1, 1000)]
    public int GeneratePermitLimit { get; init; } = 10;
}

public sealed class BranchCloudClientOptions
{
    public const string SectionName = "BranchCloud";

    [Required]
    public string BaseUrl { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
}

public sealed class BranchCredentialStoreOptions
{
    public const string SectionName = "BranchCredentialStore";

    /// <summary>InMemory | DevelopmentFile | None. Production/Staging always reject at ValidateOnStart.</summary>
    [Required]
    public string Provider { get; init; } = "None";
}
