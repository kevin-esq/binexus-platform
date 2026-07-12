using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Platform.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Binexus.IntegrationTests.Tenancy;

public sealed class AuthenticatedTenantMiddlewareTests
    : IClassFixture<PostgresTestFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "identity-integration-signing-key-with-more-than-thirty-two-bytes";
    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public AuthenticatedTenantMiddlewareTests(PostgresTestFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
            builder.UseSetting("Database:ConnectionString", postgres.ConnectionString);
            builder.UseSetting("Jwt:Issuer", "binexus");
            builder.UseSetting("Jwt:Audience", "binexus-api");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:AccessTokenLifetime", "00:15:00");
            builder.UseSetting("Jwt:RefreshTokenLifetime", "7.00:00:00");
            builder.UseSetting("Jwt:ClockSkew", "00:00:30");
            builder.UseSetting("IdentitySeed:AdminPassword", IdentitySeedDefaults.KnownInsecureDemoPassword);
        }).CreateClient();
    }

    [Fact]
    public async Task Jwt_sets_current_tenant_and_wins_over_conflicting_header()
    {
        var tokens = await LoginAsync();
        var me = await SendAsync(HttpMethod.Get, "/auth/me", tokens.AccessToken);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await me.Content.ReadFromJsonAsync<AuthSession>();
        session!.Tenant.Id.Should().NotBeEmpty();

        var probe = new HttpRequestMessage(HttpMethod.Get, "/internal/tenant-probe");
        probe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        probe.Headers.Add(DevelopmentTenantOverrideMiddleware.TenantHeader, Guid.NewGuid().ToString());
        var probeResponse = await _client.SendAsync(probe);
        probeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await probeResponse.Content.ReadFromJsonAsync<TenantProbeResponse>();
        body!.TenantId.Should().Be(session.Tenant.Id);
    }

    [Fact]
    public async Task Header_alone_works_only_for_unauthenticated_probe()
    {
        var tenantId = Guid.CreateVersion7();
        var probe = new HttpRequestMessage(HttpMethod.Get, "/internal/tenant-probe");
        probe.Headers.Add(DevelopmentTenantOverrideMiddleware.TenantHeader, tenantId.ToString());
        var response = await _client.SendAsync(probe);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TenantProbeResponse>())!.TenantId.Should().Be(tenantId);

        var inventory = await _client.GetAsync("/inventory/stock");
        inventory.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Concurrent_authenticated_requests_do_not_leak_tenant_context()
    {
        var tokens = await LoginAsync();
        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            var response = await SendAsync(HttpMethod.Get, "/auth/me", tokens.AccessToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var session = await response.Content.ReadFromJsonAsync<AuthSession>();
            return session!.Tenant.Slug;
        });
        var slugs = await Task.WhenAll(tasks);
        slugs.Should().OnlyContain(slug => slug == "acme");
    }

    private async Task<AuthTokens> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("acme", "admin@acme.test", IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>())!;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return _client.SendAsync(request);
    }

    private sealed record TenantProbeResponse(Guid? TenantId, string RequestId);
}
