using Binexus.SharedKernel.Abstractions;

namespace Binexus.Modules.Identity.Domain;

public sealed class User : ITenantScoped
{
    private User()
    {
    }

    public User(
        Guid id,
        Guid tenantId,
        string email,
        string normalizedEmail,
        string passwordHash,
        string role,
        Guid? branchId,
        bool isSystem = false,
        bool isDisabled = false)
    {
        Id = id;
        TenantId = tenantId;
        Email = email;
        NormalizedEmail = normalizedEmail;
        PasswordHash = passwordHash;
        Role = role;
        BranchId = branchId;
        IsSystem = isSystem;
        IsDisabled = isDisabled;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string Role { get; private set; } = string.Empty;

    public Guid? BranchId { get; private set; }

    public bool IsSystem { get; private set; }

    public bool IsDisabled { get; private set; }

    public Tenant Tenant { get; private set; } = null!;

    public Branch? Branch { get; private set; }

    public ICollection<RefreshToken> RefreshTokens { get; } = [];

    public void UpdatePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void SetDisabled(bool disabled) => IsDisabled = disabled;
}
