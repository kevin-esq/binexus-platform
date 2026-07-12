using System.Collections.Concurrent;
using Binexus.Modules.Identity.Domain;
using Binexus.Platform.Features.Contracts;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Binexus.Modules.Identity.Infrastructure;

/// <summary>
/// Nest FeatureFlagsService parity: short in-process cache + DB miss (default false).
/// Persists <c>tenant_features</c>; commercial API lives in Platform.Features.Contracts.
/// </summary>
public sealed class TenantFeatureService(
    BinexusDbContext db,
    IIdGenerator ids,
    TimeProvider clock) : ITenantFeatureService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    public async Task<bool> IsEnabledAsync(Guid tenantId, FeatureKey feature, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return false;
        }

        var normalizedKey = FeatureKeyValues.ToWire(feature);
        var cacheKey = CacheKey(tenantId, normalizedKey);
        var now = clock.GetUtcNow();
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > now)
        {
            return cached.Enabled;
        }

        var enabled = await db.Set<TenantFeature>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.TenantId == tenantId && x.Key == normalizedKey && x.Enabled,
                cancellationToken);

        Cache[cacheKey] = new CacheEntry(enabled, now.Add(CacheTtl));
        return enabled;
    }

    public async Task SetEnabledAsync(Guid tenantId, FeatureKey feature, bool enabled, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));

        var normalizedKey = FeatureKeyValues.ToWire(feature);
        var now = clock.GetUtcNow();
        var existing = await db.Set<TenantFeature>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Key == normalizedKey, cancellationToken);

        if (existing is null)
        {
            db.Add(new TenantFeature(ids.NewId(), tenantId, normalizedKey, enabled, now));
        }
        else
        {
            existing.SetEnabled(enabled, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        Invalidate(tenantId, normalizedKey);
    }

    public static void Invalidate(Guid tenantId, string? key = null)
    {
        if (key is null)
        {
            var prefix = $"{tenantId:D}::";
            foreach (var entry in Cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                Cache.TryRemove(entry, out _);
            }

            return;
        }

        Cache.TryRemove(CacheKey(tenantId, key.Trim().ToUpperInvariant()), out _);
    }

    private static string CacheKey(Guid tenantId, string key) => $"{tenantId:D}::{key}";

    private sealed record CacheEntry(bool Enabled, DateTimeOffset ExpiresAtUtc);
}
