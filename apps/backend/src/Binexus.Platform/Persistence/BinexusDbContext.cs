using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Messaging;
using Binexus.Platform.Tenancy;
using Binexus.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Binexus.Platform.Persistence;

public sealed class BinexusDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IReadOnlyList<IDbContextModelContributor> _modelContributors;

    public BinexusDbContext(
        DbContextOptions<BinexusDbContext> options,
        ICurrentTenant currentTenant,
        IEnumerable<IDbContextModelContributor>? modelContributors = null)
        : base(options)
    {
        _currentTenant = currentTenant;
        _modelContributors = modelContributors?.ToArray() ?? [];
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<EventHandlerDelivery> EventHandlerDeliveries => Set<EventHandlerDelivery>();

    public DbSet<BranchInstance> BranchInstances => Set<BranchInstance>();

    public DbSet<BranchActivation> BranchActivations => Set<BranchActivation>();

    public DbSet<CloudBranchInstance> CloudBranchInstances => Set<CloudBranchInstance>();

    public DbSet<BranchActivationChallenge> BranchActivationChallenges => Set<BranchActivationChallenge>();

    public DbSet<DevicePairingSession> DevicePairingSessions => Set<DevicePairingSession>();

    public DbSet<DevicePairingChallenge> DevicePairingChallenges => Set<DevicePairingChallenge>();

    public DbSet<DevicePairingRequest> DevicePairingRequests => Set<DevicePairingRequest>();

    public DbSet<BranchDevice> BranchDevices => Set<BranchDevice>();

    public DbSet<BranchTerminal> BranchTerminals => Set<BranchTerminal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BinexusDbContext).Assembly);
        foreach (var contributor in _modelContributors)
        {
            contributor.Configure(modelBuilder);
        }

        ApplyTenantQueryFilters(modelBuilder);
    }

    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType == typeof(OutboxMessage)
                || entityType.ClrType == typeof(EventHandlerDelivery))
            {
                continue;
            }

            if (!typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var method = typeof(BinexusDbContext)
                .GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType);

            method.Invoke(this, [modelBuilder]);
        }
    }

    private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            CurrentTenantId == null || e.TenantId == CurrentTenantId);
    }

    private Guid? CurrentTenantId => _currentTenant.Current?.TenantId;
}
