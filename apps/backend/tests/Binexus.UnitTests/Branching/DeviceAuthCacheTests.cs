using System.Reflection;
using Binexus.Platform.Branching.DeviceAuth;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace Binexus.UnitTests.Branching;

/// <summary>
/// Cache outage behavior is intentionally implemented at the resolver boundary:
/// an unexpired entry may be served without PostgreSQL; a miss maps to
/// <c>DEVICE_STATUS_UNAVAILABLE</c>. Integration coverage exercises eviction.
/// </summary>
public sealed class DeviceAuthCacheTests
{
    [Fact]
    public void Evict_removes_the_device_status_snapshot()
    {
        var instanceId = Guid.CreateVersion7();
        var deviceId = Guid.CreateVersion7();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cacheKey = $"device-auth:{instanceId:D}:{deviceId:D}";
        cache.Set(
            cacheKey,
            new DeviceStatusSnapshot(
                "Active",
                Guid.NewGuid().ToString("N"),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7()));
        var resolver = new DeviceStatusResolver(null!, null!, cache, null!);

        resolver.Evict(instanceId, deviceId);

        cache.TryGetValue(cacheKey, out _).Should().BeFalse();
    }

    [Fact]
    public void Device_status_cache_key_is_instance_and_device_scoped()
    {
        var instanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var deviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var method = typeof(DeviceStatusResolver).GetMethod(
            "CacheKey",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var key = (string)method.Invoke(null, [instanceId, deviceId])!;

        key.Should().Be("device-auth:11111111-1111-1111-1111-111111111111:22222222-2222-2222-2222-222222222222");
    }
}
