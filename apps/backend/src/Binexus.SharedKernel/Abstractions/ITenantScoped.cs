namespace Binexus.SharedKernel.Abstractions;

/// <summary>Entities scoped to a tenant carry this marker for global query filters.</summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}
