namespace Binexus.Modules.Identity.Domain;

public sealed class Tenant
{
    private Tenant()
    {
    }

    public Tenant(Guid id, string slug, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Slug = slug;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Slug { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public ICollection<Branch> Branches { get; } = [];

    public ICollection<User> Users { get; } = [];
}
