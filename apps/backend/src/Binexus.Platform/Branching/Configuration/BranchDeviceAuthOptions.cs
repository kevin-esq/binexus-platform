using System.ComponentModel.DataAnnotations;

namespace Binexus.Platform.Branching.Configuration;

/// <summary>
/// Branch-signed Device Access Token (DAT) configuration.
/// HMAC keys never leave the Branch Runtime. Distinct from Jwt:SigningKey and pairing peppers.
/// Limitation (v1): any process that can validate with the HMAC key can also mint DATs.
/// </summary>
public sealed class BranchDeviceAuthOptions
{
    public const string SectionName = "BranchDeviceAuth";

    [Required]
    [MinLength(1)]
    public string CurrentKeyId { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<BranchDeviceAuthSigningKey> SigningKeys { get; set; } = [];

    [Range(60, 3600)]
    public int TokenLifetimeSeconds { get; set; } = 300;

    [Range(0, 120)]
    public int ClockSkewSeconds { get; set; } = 30;

    [Range(1, 120)]
    public int StatusCacheSeconds { get; set; } = 15;

    [Range(15, 300)]
    public int ChallengeTtlSeconds { get; set; } = 60;

    /// <summary>
    /// When true, HTTP without TLS is allowed (lab). Production must not rely on this;
    /// hard HTTPS + pinning belongs to LAN TLS AND BRANCH SERVER IDENTITY.
    /// </summary>
    public bool AllowInsecureBranchTransport { get; set; }

    /// <summary>Combined IP+DeviceId+global window length in seconds (default 60).</summary>
    [Range(1, 600)]
    public int RateLimitWindowSeconds { get; set; } = 60;

    /// <summary>Max challenges/tokens per IP per window (also bound as MachinePermitLimit for compatibility).</summary>
    [Range(1, 1000)]
    public int IpPermitLimit { get; set; } = 30;

    /// <summary>Max challenges/tokens per normalized DeviceId per window.</summary>
    [Range(1, 1000)]
    public int DevicePermitLimit { get; set; } = 20;

    /// <summary>Max challenges/tokens globally per window for device-auth endpoints.</summary>
    [Range(1, 5000)]
    public int GlobalPermitLimit { get; set; } = 120;

    /// <summary>Compatibility alias for <see cref="IpPermitLimit"/>.</summary>
    [Range(1, 1000)]
    public int MachinePermitLimit { get; set; } = 30;
}

public sealed class BranchDeviceAuthSigningKey
{
    [Required]
    [MinLength(1)]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>UTF-8 secret material; minimum 32 characters.</summary>
    [Required]
    [MinLength(32)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// When true, this key is lab/dev-only and must never be selected in Production/Staging.
    /// </summary>
    public bool LabOnly { get; set; }
}
