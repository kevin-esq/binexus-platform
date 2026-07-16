using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Binexus.IntegrationTests.Api;

public sealed class TenantProbeProductionTests : IClassFixture<ProductionWebApplicationFactory>
{
    private readonly HttpClient _client;

    public TenantProbeProductionTests(ProductionWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task Tenant_probe_returns_not_found_in_production()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/internal/tenant-probe");
        request.Headers.Add("X-Binexus-Tenant-Id", Guid.CreateVersion7().ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public sealed class ProductionWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:ConnectionString",
            "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
        builder.UseSetting("Binexus:RuntimeMode", "Cloud");
        builder.UseSetting(
            "Jwt:SigningKey",
            "production-test-signing-key-with-more-than-thirty-two-bytes");
        builder.UseSetting(
            "CloudActivation:CodePepper",
            "production-test-cloud-activation-pepper-32chars");
        builder.UseSetting("Logistics:Storage:Provider", "MinIO");
        builder.UseSetting("Logistics:Storage:Endpoint", "http://127.0.0.1:9000");
        builder.UseSetting("Logistics:Storage:Bucket", "binexus-test");
        builder.UseSetting("Logistics:Storage:AccessKey", "test-access-key");
        builder.UseSetting("Logistics:Storage:SecretKey", "test-secret-key");
        builder.UseEnvironment(Environments.Production);
    }
}
