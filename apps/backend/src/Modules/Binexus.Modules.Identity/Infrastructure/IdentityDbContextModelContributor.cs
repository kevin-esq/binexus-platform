using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Binexus.Modules.Identity.Infrastructure;

public sealed class IdentityDbContextModelContributor : IDbContextModelContributor
{
    public void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContextModelContributor).Assembly);
}
