using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.DeviceAuth;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Binexus.UnitTests.Branching;

public sealed class DeviceAuthCacheFailureTests
{
    [Fact]
    public async Task Resolve_returns_unexpired_cached_snapshot_when_database_is_unavailable()
    {
        var instanceId = Guid.CreateVersion7();
        var deviceId = Guid.CreateVersion7();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var expected = new DeviceStatusSnapshot(
            "Active", Guid.NewGuid().ToString("N"), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        cache.Set(CacheKey(instanceId, deviceId), expected, TimeSpan.FromMinutes(1));

        await using var db = CreateDisposedContext();
        var resolver = CreateResolver(db, cache, instanceId);

        var actual = await resolver.ResolveAsync(instanceId, deviceId, CancellationToken.None);

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task Resolve_returns_status_unavailable_when_cache_is_empty_and_database_is_unavailable()
    {
        var instanceId = Guid.CreateVersion7();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var db = CreateDisposedContext();
        var resolver = CreateResolver(db, cache, instanceId);

        var act = () => resolver.ResolveAsync(instanceId, Guid.CreateVersion7(), CancellationToken.None);

        await act.Should().ThrowAsync<DeviceAuthException>()
            .Where(x => x.Code == DeviceAuthErrorCodes.DeviceStatusUnavailable);
    }

    [Fact]
    public async Task Resolve_returns_status_unavailable_when_cached_snapshot_has_expired()
    {
        var instanceId = Guid.CreateVersion7();
        var deviceId = Guid.CreateVersion7();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(
            CacheKey(instanceId, deviceId),
            new DeviceStatusSnapshot("Active", "stamp", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()),
            DateTimeOffset.UtcNow.AddSeconds(-1));
        await using var db = CreateDisposedContext();
        var resolver = CreateResolver(db, cache, instanceId);

        var act = () => resolver.ResolveAsync(instanceId, deviceId, CancellationToken.None);

        await act.Should().ThrowAsync<DeviceAuthException>()
            .Where(x => x.Code == DeviceAuthErrorCodes.DeviceStatusUnavailable);
    }

    private static DeviceStatusResolver CreateResolver(BinexusDbContext db, IMemoryCache cache, Guid instanceId) =>
        new(
            db,
            new StaticBranchInstanceAccessor(instanceId),
            cache,
            Options.Create(new BranchDeviceAuthOptions { StatusCacheSeconds = 60 }),
            NullLogger<DeviceStatusResolver>.Instance);

    private static BinexusDbContext CreateDisposedContext()
    {
        var db = new BinexusDbContext(
            new DbContextOptionsBuilder<BinexusDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            new CurrentTenant());
        db.Dispose();
        return db;
    }

    private static string CacheKey(Guid instanceId, Guid deviceId) =>
        $"device-auth:{instanceId:D}:{deviceId:D}";

    private sealed class StaticBranchInstanceAccessor(Guid instanceId) : IBranchInstanceAccessor
    {
        public ValueTask<BranchInstanceInfo> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new BranchInstanceInfo(
                instanceId,
                BranchServerStatus.Active,
                Guid.CreateVersion7(),
                Guid.CreateVersion7()));
    }
}
