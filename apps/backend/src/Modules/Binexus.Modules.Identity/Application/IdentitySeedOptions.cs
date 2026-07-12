namespace Binexus.Modules.Identity.Application;

/// <summary>
/// Demo seed credentials are never stored in appsettings. The known insecure
/// placeholder is only allowed as a Testing default and is rejected in Production/Staging.
/// </summary>
public static class IdentitySeedDefaults
{
    /// <summary>Well-known insecure demo password. Never use outside Testing/Development.</summary>
    public const string KnownInsecureDemoPassword = "ChangeMe123!";

    /// <summary>
    /// Well-known insecure JWT signing key from <c>.env.example</c>.
    /// Allowed only in Development/Testing — rejected in Staging/Production.
    /// </summary>
    public const string KnownInsecureLocalSigningKey =
        "local-build-signing-key-with-more-than-thirty-two-bytes";

    public const string DefaultTenantSlug = "acme";

    public const string DefaultTenantName = "Acme";

    public const string DefaultAdminEmail = "admin@acme.test";

    public const string DefaultBranchName = "Main";
}

public sealed class IdentitySeedOptions
{
    public const string SectionName = "IdentitySeed";

    public string TenantSlug { get; set; } = IdentitySeedDefaults.DefaultTenantSlug;

    public string TenantName { get; set; } = IdentitySeedDefaults.DefaultTenantName;

    public string AdminEmail { get; set; } = IdentitySeedDefaults.DefaultAdminEmail;

    public string BranchName { get; set; } = IdentitySeedDefaults.DefaultBranchName;

    /// <summary>Required for Development seed. Testing may fall back to the known demo password.</summary>
    public string? AdminPassword { get; set; }
}
