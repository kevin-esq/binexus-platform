using System.Net;
using Binexus.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Binexus.IntegrationTests.Api;

public sealed class HealthEndpointTests : IClassFixture<CloudApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(CloudApiFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ConnectionString",
                "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
        }).CreateClient();
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
