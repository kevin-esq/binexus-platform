using System.Net;
using System.Net.Http.Json;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Runtime;
using Binexus.Workers.Hosting;
using Binexus.Workers.Outbox;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Binexus.Workers.Tests.Runtime;

public sealed class WorkersRuntimeHostTests
{
    [Fact]
    public async Task Workers_cloud_starts_and_reports_runtime()
    {
        var builder = WorkersHost.CreateBuilder(BaseConfig("Cloud"));
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        await WorkersHost.InitializeAsync(app);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await app.StartAsync(cts.Token);

        var client = app.GetTestClient();
        var response = await client.GetAsync("/health/runtime", cts.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RuntimeHealthResponse>(cancellationToken: cts.Token);
        body!.RuntimeMode.Should().Be("Cloud");

        app.Services.GetServices<IRuntimeDescriptor>().Should().ContainSingle();
        (await client.GetAsync("/health/branch", cts.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await app.StopAsync(cts.Token);
    }

    [Theory]
    [InlineData("Cloud")]
    [InlineData("Branch")]
    public void Workers_registers_outbox_hosted_service_once(string mode)
    {
        var builder = WorkersHost.CreateBuilder(BaseConfig(mode));
        builder.Services.AddHostedService<OutboxWorkerHost>();
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetServices<IHostedService>().OfType<OutboxWorkerHost>().Should().ContainSingle();
        provider.GetServices<IRuntimeDescriptor>().Should().ContainSingle();
    }

    [Fact]
    public void Workers_branch_registers_instance_accessor_cloud_does_not()
    {
        var branch = WorkersHost.CreateBuilder(BaseConfig("Branch")).Services.BuildServiceProvider();
        branch.GetService<IBranchInstanceAccessor>().Should().NotBeNull();
        branch.GetService<IBranchInstanceInitializer>().Should().NotBeNull();

        var cloud = WorkersHost.CreateBuilder(BaseConfig("Cloud")).Services.BuildServiceProvider();
        cloud.GetService<IBranchInstanceAccessor>().Should().BeNull();
        cloud.GetService<IBranchInstanceInitializer>().Should().BeNull();
    }

    [Fact]
    public void Workers_missing_runtime_mode_fails_at_composition()
    {
        var previous = Environment.GetEnvironmentVariable("Binexus__RuntimeMode");
        try
        {
            Environment.SetEnvironmentVariable("Binexus__RuntimeMode", null);
            var action = () => WorkersHost.CreateBuilder(BaseConfig(runtimeMode: null));
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*Binexus:RuntimeMode*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Binexus__RuntimeMode", previous);
        }
    }

    [Fact]
    public void Workers_invalid_runtime_mode_fails_at_composition()
    {
        var action = () => WorkersHost.CreateBuilder(BaseConfig("Nope"));
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*invalid*");
    }

    private static Dictionary<string, string?> BaseConfig(string? runtimeMode)
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] =
                "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus",
            ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
            ["Jwt:Issuer"] = "binexus",
            ["Jwt:Audience"] = "binexus-api",
            ["Jwt:SigningKey"] = "integration-test-signing-key-with-more-than-32-bytes",
            ["Jwt:AccessTokenDuration"] = "00:15:00",
            ["Jwt:RefreshTokenDuration"] = "7.00:00:00",
            ["Jwt:ClockSkew"] = "00:00:30",
            ["OutboxWorker:PollInterval"] = "01:00:00",
            ["OutboxWorker:BatchSize"] = "10",
        };
        if (runtimeMode is not null)
        {
            values["Binexus:RuntimeMode"] = runtimeMode;
        }

        return values;
    }
}
