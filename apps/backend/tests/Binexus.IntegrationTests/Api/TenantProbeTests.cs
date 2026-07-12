using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Binexus.IntegrationTests.Api;

public sealed class TenantProbeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public TenantProbeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ConnectionString",
                "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
            builder.UseEnvironment(Environments.Development);
        }).CreateClient();
    }

    [Fact]
    public async Task Tenant_probe_resolves_header_in_development()
    {
        var tenantId = Guid.Parse("11111111-1111-7111-8111-111111111111");
        var request = new HttpRequestMessage(HttpMethod.Get, "/internal/tenant-probe");
        request.Headers.Add("X-Binexus-Tenant-Id", tenantId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<TenantProbeResponse>();
        body.Should().NotBeNull();
        body!.TenantId.Should().Be(tenantId);
    }

    private sealed record TenantProbeResponse(Guid? TenantId, string RequestId);
}
