using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Logistics.Application;
using Binexus.Platform.Features.Contracts;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Logistics;

[Collection("postgres")]
public sealed class TenantFeatureLiquidationTests : IClassFixture<PostgresTestFixture>, IClassFixture<CloudApiFactory>
{
    private const string SigningKey = "liquidation-feature-signing-key-with-more-than-thirty-two-bytes";
    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public TenantFeatureLiquidationTests(PostgresTestFixture postgres, CloudApiFactory factory)
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
            builder.UseSetting("Features:LiquidationKillSwitch", "true");
            builder.UseSetting("Logistics:Storage:Provider", "Local");
        }).CreateClient();
    }

    [Fact]
    public async Task Tenant_with_liquidation_entitlement_can_reach_liquidation_validation()
    {
        var acme = await AcmeContextAsync();
        await SetFeatureAsync(acme.TenantId, true);

        var response = await LiquidateAsync(await LoginAsync("acme", "admin@acme.test"), Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Disabled_tenant_receives_feature_disabled()
    {
        var tenant = await CreateTenantUserAsync(RoleNames.Admin);
        await SetFeatureAsync(tenant.TenantId, false);

        var response = await LiquidateAsync(await LoginAsync(tenant.Slug, tenant.Email), Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(response)).Should().Be("FEATURE_DISABLED");
    }

    [Fact]
    public async Task Admin_without_entitlement_receives_feature_disabled()
    {
        var tenant = await CreateTenantUserAsync(RoleNames.Admin);

        var response = await LiquidateAsync(await LoginAsync(tenant.Slug, tenant.Email), Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(response)).Should().Be("FEATURE_DISABLED");
    }

    [Fact]
    public async Task Entitled_cashier_receives_liquidation_forbidden()
    {
        var tenant = await CreateTenantUserAsync(RoleNames.Cashier);
        await SetFeatureAsync(tenant.TenantId, true);

        var response = await LiquidateAsync(await LoginAsync(tenant.Slug, tenant.Email), Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(response)).Should().Be("LIQUIDATION_FORBIDDEN");
    }

    [Fact]
    public async Task Entitled_super_admin_can_reach_liquidation_validation()
    {
        var tenant = await CreateTenantUserAsync(RoleNames.SuperAdmin);
        await SetFeatureAsync(tenant.TenantId, true);

        var response = await LiquidateAsync(await LoginAsync(tenant.Slug, tenant.Email), Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Disabled_liquidation_does_not_block_candidate_reads_or_route_creation()
    {
        var acme = await AcmeContextAsync();
        await SetFeatureAsync(acme.TenantId, false);
        var token = await LoginAsync("acme", "admin@acme.test");

        var candidates = await SendAsync(HttpMethod.Get, "/logistics/delivery-route-candidates?status=READY", token);
        var create = await SendAsync(HttpMethod.Post, "/logistics/delivery-routes", token, new CreateDeliveryRouteRequest(acme.BranchId, null));

        candidates.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tenant_feature_lookup_does_not_leak_to_another_tenant()
    {
        var acme = await AcmeContextAsync();
        var other = await CreateTenantUserAsync(RoleNames.Admin);
        await SetFeatureAsync(acme.TenantId, true);

        using var scope = _postgres.CreateScope();
        var features = scope.ServiceProvider.GetRequiredService<ITenantFeatureService>();

        (await features.IsEnabledAsync(other.TenantId, FeatureKey.Liquidation)).Should().BeFalse();
    }

    private async Task<HttpResponseMessage> LiquidateAsync(string token, Guid routeId) =>
        await SendAsync(HttpMethod.Post, $"/logistics/delivery-routes/{routeId}/liquidate", token, new LiquidateDeliveryRouteRequest(0, null, null, null));

    private async Task SetFeatureAsync(Guid tenantId, bool enabled)
    {
        using var scope = _postgres.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantFeatureService>()
            .SetEnabledAsync(tenantId, FeatureKey.Liquidation, enabled);
    }

    private async Task<(Guid TenantId, Guid BranchId)> AcmeContextAsync()
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Main");
        return (tenant.Id, branch.Id);
    }

    private async Task<(Guid TenantId, string Slug, string Email)> CreateTenantUserAsync(string role)
    {
        var slug = $"liquidation-{Guid.NewGuid():N}";
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@liquidation.test";
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tenant = new Tenant(ids.NewId(), slug, slug, DateTimeOffset.UtcNow);
        var branch = new Branch(ids.NewId(), tenant.Id, "Main");
        db.AddRange(tenant, branch, new User(ids.NewId(), tenant.Id, email, EmailNormalizer.Normalize(email),
            await hasher.HashAsync(IdentitySeedDefaults.KnownInsecureDemoPassword), role, branch.Id));
        await db.SaveChangesAsync();
        return (tenant.Id, slug, email);
    }

    private async Task<string> LoginAsync(string slug, string email)
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(slug, email, IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>())!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString()!;
    }
}
