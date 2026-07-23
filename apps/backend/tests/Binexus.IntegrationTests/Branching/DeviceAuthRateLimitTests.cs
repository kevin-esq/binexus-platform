using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Binexus.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Binexus.IntegrationTests.Branching;

/// <summary>
/// Deterministic Branch device-auth rate limits (IP + DeviceId + global).
/// Low permits via UseSetting; IP partitions via Testing-only X-Binexus-Test-Remote-Ip.
/// </summary>
[Collection("postgres")]
public sealed class DeviceAuthRateLimitTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Same_ip_and_device_hits_device_limit_with_generic_429()
    {
        await using var context = await StartBranchAsync(ip: 2, device: 2, global: 50, windowSeconds: 60);
        using var machine = CreateMachine(context, "203.0.113.10");
        var deviceId = Guid.CreateVersion7();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 3; i++)
        {
            last = await PostChallengeAsync(machine, deviceId);
        }

        await AssertRateLimitedAsync(last!);
    }

    [Fact]
    public async Task Same_ip_different_device_ids_are_partitioned_independently_until_ip_limit()
    {
        await using var context = await StartBranchAsync(ip: 50, device: 1, global: 50, windowSeconds: 60);
        using var machine = CreateMachine(context, "203.0.113.11");
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        (await PostChallengeAsync(machine, first)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        await AssertRateLimitedAsync(await PostChallengeAsync(machine, first));

        // Fresh DeviceId partition on the same IP.
        (await PostChallengeAsync(machine, second)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        await AssertRateLimitedAsync(await PostChallengeAsync(machine, second));
    }

    [Fact]
    public async Task Same_ip_many_devices_eventually_hit_ip_limit()
    {
        await using var context = await StartBranchAsync(ip: 2, device: 50, global: 50, windowSeconds: 60);
        using var machine = CreateMachine(context, "203.0.113.12");

        (await PostChallengeAsync(machine, Guid.CreateVersion7())).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await PostChallengeAsync(machine, Guid.CreateVersion7())).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        await AssertRateLimitedAsync(await PostChallengeAsync(machine, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Different_ips_same_device_id_are_partitioned_by_device_across_ips()
    {
        await using var context = await StartBranchAsync(ip: 50, device: 2, global: 50, windowSeconds: 60);
        using var ipA = CreateMachine(context, "203.0.113.20");
        using var ipB = CreateMachine(context, "203.0.113.21");
        var deviceId = Guid.CreateVersion7();

        (await PostChallengeAsync(ipA, deviceId)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await PostChallengeAsync(ipB, deviceId)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        // Third call with same DeviceId (any IP) trips device partition
        await AssertRateLimitedAsync(await PostChallengeAsync(ipA, deviceId));
    }

    [Fact]
    public async Task Global_limit_applies_across_ips_and_devices()
    {
        await using var context = await StartBranchAsync(ip: 50, device: 50, global: 2, windowSeconds: 60);
        using var ipA = CreateMachine(context, "203.0.113.30");
        using var ipB = CreateMachine(context, "203.0.113.31");

        (await PostChallengeAsync(ipA, Guid.CreateVersion7())).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await PostChallengeAsync(ipB, Guid.CreateVersion7())).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        await AssertRateLimitedAsync(await PostChallengeAsync(ipA, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Invalid_device_ids_collapse_to_shared_bucket()
    {
        await using var context = await StartBranchAsync(ip: 50, device: 2, global: 50, windowSeconds: 60);
        using var machine = CreateMachine(context, "203.0.113.40");

        for (var i = 0; i < 2; i++)
        {
            var response = await machine.PostAsJsonAsync(
                "/branch/device-auth/challenges",
                new { deviceId = $"not-a-guid-{i}" });
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        var third = await machine.PostAsJsonAsync(
            "/branch/device-auth/challenges",
            new { deviceId = "still-invalid" });
        await AssertRateLimitedAsync(third);
    }

    [Fact]
    public async Task Rate_limit_window_resets_after_configured_seconds()
    {
        await using var context = await StartBranchAsync(ip: 50, device: 1, global: 50, windowSeconds: 1);
        using var machine = CreateMachine(context, "203.0.113.50");
        var deviceId = Guid.CreateVersion7();

        (await PostChallengeAsync(machine, deviceId)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        await AssertRateLimitedAsync(await PostChallengeAsync(machine, deviceId));

        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        (await PostChallengeAsync(machine, deviceId)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Rate_limit_response_does_not_reveal_device_lifecycle_state()
    {
        await using var context = await StartBranchAsync(ip: 1, device: 1, global: 1, windowSeconds: 60);
        using var machine = CreateMachine(context, "203.0.113.60");

        _ = await PostChallengeAsync(machine, Guid.CreateVersion7());
        var limited = await PostChallengeAsync(machine, Guid.CreateVersion7());
        await AssertRateLimitedAsync(limited);

        var body = await limited.Content.ReadAsStringAsync();
        body.Should().Contain("RATE_LIMITED");
        body.Should().NotContainEquivalentOf("Active");
        body.Should().NotContainEquivalentOf("Revoked");
        body.Should().NotContainEquivalentOf("Pending");
        body.Should().NotContainEquivalentOf("inexistent");
        body.Should().NotContainEquivalentOf("fingerprint");
    }

    private async Task<DeviceAuthEndToEndTests.BranchContext> StartBranchAsync(
        int ip,
        int device,
        int global,
        int windowSeconds)
    {
        var helper = new DeviceAuthEndToEndTests(fixture);
        return await helper.StartBranchAsync(builder =>
        {
            builder.UseSetting("BranchDeviceAuth:IpPermitLimit", ip.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("BranchDeviceAuth:MachinePermitLimit", ip.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("BranchDeviceAuth:DevicePermitLimit", device.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("BranchDeviceAuth:GlobalPermitLimit", global.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("BranchDeviceAuth:RateLimitWindowSeconds", windowSeconds.ToString(CultureInfo.InvariantCulture));
        });
    }

    private static HttpClient CreateMachine(DeviceAuthEndToEndTests.BranchContext context, string ip)
    {
        var client = context.Factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Binexus-Test-Remote-Ip", ip);
        return client;
    }

    private static Task<HttpResponseMessage> PostChallengeAsync(HttpClient machine, Guid deviceId) =>
        machine.PostAsJsonAsync("/branch/device-auth/challenges", new { deviceId });

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Should().ContainKey("Retry-After");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("RATE_LIMITED");
        body.Should().Contain("rate-limited");
    }
}
