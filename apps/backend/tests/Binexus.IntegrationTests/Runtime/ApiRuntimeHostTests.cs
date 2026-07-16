using System.Net;
using System.Net.Http.Json;
using Binexus.Platform.Runtime;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Binexus.IntegrationTests.Runtime;

public sealed class ApiRuntimeHostTests
{
    [Fact]
    public async Task Api_cloud_starts_and_reports_runtime()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Binexus:RuntimeMode", "Cloud");
            builder.UseSetting("Database:ConnectionString",
                "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            builder.UseSetting("CloudActivation:CodePepper", "integration-test-cloud-activation-pepper-32b");
            builder.UseSetting("SEED_ON_START", "0");
        });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/runtime");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RuntimeHealthResponse>();
        body!.RuntimeMode.Should().Be("Cloud");

        using var scope = factory.Services.CreateScope();
        var descriptors = scope.ServiceProvider.GetServices<IRuntimeDescriptor>().ToList();
        descriptors.Should().ContainSingle();
        descriptors[0].Mode.Should().Be(RuntimeMode.Cloud);

        factory.Services.GetServices<IHostedService>()
            .Select(s => s.GetType().Name)
            .Should()
            .NotContain("OutboxWorkerHost");

        (await client.GetAsync("/health/branch")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Api_missing_runtime_mode_fails()
    {
        var previous = Environment.GetEnvironmentVariable("Binexus__RuntimeMode");
        try
        {
            Environment.SetEnvironmentVariable("Binexus__RuntimeMode", null);

            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.UseSetting("Database:ConnectionString",
                        "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
                    builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
                });

            var action = () => factory.CreateClient();

            action.Should().Throw<Exception>()
                .Which.GetBaseException().Message.Should().Contain("Binexus:RuntimeMode");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Binexus__RuntimeMode", previous);
        }
    }

    [Fact]
    public void Api_invalid_runtime_mode_fails()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("Binexus:RuntimeMode", "Local");
                builder.UseSetting("Database:ConnectionString",
                    "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
                builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            });

        var action = () => factory.CreateClient();

        action.Should().Throw<Exception>()
            .Which.GetBaseException().Message.Should().Contain("invalid");
    }
}
