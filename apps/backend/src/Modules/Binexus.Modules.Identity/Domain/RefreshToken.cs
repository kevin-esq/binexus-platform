using Binexus.SharedKernel.Abstractions;

namespace Binexus.Modules.Identity.Domain;

public sealed class RefreshToken : ITenantScoped
{
    private RefreshToken()
    {
    }

    public RefreshToken(
        Guid id,
        Guid tenantId,
        Guid userId,
        string tokenHash,
        Guid familyId,
        Guid? parentTokenId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        ParentTokenId = parentTokenId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public Guid FamilyId { get; private set; }

    public Guid? ParentTokenId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public string? RevocationReason { get; private set; }

    public User User { get; private set; } = null!;
}
