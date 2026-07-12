using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Platform.Features.Contracts;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Binexus.Modules.Identity.Infrastructure;

/// <summary>
/// Development/Testing-only demo seed. Never registered in Production or Staging.
/// </summary>
public sealed class DevelopmentIdentitySeeder(
    BinexusDbContext dbContext,
    IIdGenerator idGenerator,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    IOptions<IdentitySeedOptions> seedOptions,
    IHostEnvironment environment,
    ILogger<DevelopmentIdentitySeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "Identity demo seed is only available in Development or Testing environments.");
        }

        var options = seedOptions.Value;
        var password = ResolvePassword(options, environment, logger);

        await dbContext.Database.MigrateAsync(cancellationToken);

        var slug = options.TenantSlug.Trim().ToLowerInvariant();
        var tenant = await dbContext.Set<Tenant>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);
        if (tenant is null)
        {
            tenant = new Tenant(idGenerator.NewId(), slug, options.TenantName.Trim(), timeProvider.GetUtcNow());
            dbContext.Add(tenant);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var branch = await dbContext.Set<Branch>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenant.Id && x.Name == options.BranchName,
                cancellationToken);
        if (branch is null)
        {
            branch = new Branch(idGenerator.NewId(), tenant.Id, options.BranchName);
            dbContext.Add(branch);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var normalizedEmail = EmailNormalizer.Normalize(options.AdminEmail);
        var user = await dbContext.Set<User>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenant.Id && x.NormalizedEmail == normalizedEmail,
                cancellationToken);
        if (user is null)
        {
            var email = options.AdminEmail.Trim();
            dbContext.Add(new User(
                idGenerator.NewId(),
                tenant.Id,
                email,
                normalizedEmail,
                await passwordHasher.HashAsync(password, cancellationToken),
                RoleNames.SuperAdmin,
                branch.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            SeedLog.SeedCreated(logger, slug, email);
        }

        await UpsertTenantFeaturesAsync(tenant.Id, cancellationToken);

        // Development demo only: operator POS + COD liquidation. Testing keeps all false
        // so integration tests enable features explicitly.
        if (environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            await EnableDemoOperatorFeaturesAsync(tenant.Id, cancellationToken);
        }
    }

    private async Task UpsertTenantFeaturesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var existing = await dbContext.Set<TenantFeature>()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(x => x.Key, StringComparer.Ordinal);

        foreach (var key in FeatureKeyValues.All)
        {
            if (byKey.ContainsKey(key))
            {
                continue;
            }

            dbContext.Add(new TenantFeature(idGenerator.NewId(), tenantId, key, enabled: false, now));
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EnableDemoOperatorFeaturesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        string[] demoKeys = [FeatureKeyValues.PosRetail, FeatureKeyValues.Liquidation];
        var features = await dbContext.Set<TenantFeature>()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && demoKeys.Contains(x.Key))
            .ToListAsync(cancellationToken);

        foreach (var feature in features)
        {
            if (!feature.Enabled)
            {
                feature.SetEnabled(true, now);
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            SeedLog.DemoFeaturesEnabled(logger, tenantId);
        }
    }

    internal static string ResolvePassword(
        IdentitySeedOptions options,
        IHostEnvironment environment,
        ILogger logger)
    {
        var password = options.AdminPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            if (environment.IsEnvironment("Testing"))
            {
                return IdentitySeedDefaults.KnownInsecureDemoPassword;
            }

            throw new InvalidOperationException(
                "IdentitySeed:AdminPassword must be set via user-secrets or environment for Development seed.");
        }

        if (string.Equals(
                password,
                IdentitySeedDefaults.KnownInsecureDemoPassword,
                StringComparison.Ordinal)
            && !environment.IsEnvironment("Testing"))
        {
            SeedLog.KnownPasswordWarning(logger);
        }

        return password;
    }
}

internal static partial class SeedLog
{
    [LoggerMessage(2001, LogLevel.Information, "Identity demo seed created tenant {TenantSlug} and admin {AdminEmail}")]
    public static partial void SeedCreated(ILogger logger, string tenantSlug, string adminEmail);

    [LoggerMessage(
        2002,
        LogLevel.Warning,
        "Identity demo seed is using the well-known insecure development password placeholder. Set IdentitySeed:AdminPassword via user-secrets or environment before any shared Development deployment.")]
    public static partial void KnownPasswordWarning(ILogger logger);

    [LoggerMessage(
        2003,
        LogLevel.Information,
        "Identity demo seed enabled POS_RETAIL and LIQUIDATION for tenant {TenantId}")]
    public static partial void DemoFeaturesEnabled(ILogger logger, Guid tenantId);
}
