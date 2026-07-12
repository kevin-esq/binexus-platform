using Binexus.SharedKernel.Abstractions;

namespace Binexus.Modules.Identity.Domain;

public sealed class Branch : ITenantScoped
{
    private Branch()
    {
    }

    public Branch(Guid id, Guid tenantId, string name)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Tenant Tenant { get; private set; } = null!;

    public ICollection<User> Users { get; } = [];
}
