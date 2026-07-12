using Microsoft.EntityFrameworkCore;

namespace Binexus.Platform.Persistence;

/// <summary>
/// Modules contribute EF configurations without Platform referencing module assemblies (ADR-0017).
/// </summary>
public interface IDbContextModelContributor
{
    void Configure(ModelBuilder modelBuilder);
}
