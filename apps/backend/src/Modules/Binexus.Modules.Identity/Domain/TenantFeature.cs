namespace Binexus.Modules.Identity.Domain;

/// <summary>
/// Per-tenant commercial entitlement row (ADR-0009). Unique on (TenantId, Key).
/// </summary>
public sealed class TenantFeature
{
    private TenantFeature()
    {
    }

    public TenantFeature(Guid id, Guid tenantId, string key, bool enabled, DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));
        Id = id;
        TenantId = tenantId;
        Key = key.Trim().ToUpperInvariant();
        Enabled = enabled;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void SetEnabled(bool enabled, DateTimeOffset updatedAtUtc)
    {
        Enabled = enabled;
        UpdatedAtUtc = updatedAtUtc;
    }
}
