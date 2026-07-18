using Binexus.Composition;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Infrastructure;
using Binexus.Platform.DependencyInjection;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Binexus.IntegrationTests.Infrastructure;

public sealed class PostgresTestFixture : IAsyncLifetime
{
    /// <summary>
    /// When set, skip Testcontainers and use this connection (Windows CI cannot run Linux images).
    /// </summary>
    public const string ExternalConnectionEnvironmentVariable = "BINEXUS_TEST_DATABASE_CONNECTION";

    private PostgreSqlContainer? _container;
    private string _connectionString = string.Empty;

    public string ConnectionString =>
        string.IsNullOrEmpty(_connectionString)
            ? throw new InvalidOperationException("PostgresTestFixture has not been initialized.")
            : _connectionString;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            _connectionString = external;
        }
        else
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("binexus_test")
                .WithUsername("binexus")
                .WithPassword("binexus")
                .Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }

        await ApplyMigrationsAsync();
        using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentIdentitySeeder>().SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public async Task ApplyMigrationsAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task ResetOutboxAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE event_handler_deliveries, outbox_messages");
    }

    public IServiceScope CreateScope(
        Action<IServiceCollection>? configure = null,
        string environmentName = "Testing")
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString,
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                ["ASPNETCORE_ENVIRONMENT"] = environmentName,
                ["Binexus:RuntimeMode"] = "Cloud",
                ["Jwt:Issuer"] = "binexus",
                ["Jwt:Audience"] = "binexus-api",
                ["Jwt:SigningKey"] = "integration-test-signing-key-with-more-than-32-bytes",
                ["Jwt:AccessTokenLifetime"] = "00:15:00",
                ["Jwt:RefreshTokenLifetime"] = "7.00:00:00",
                ["Jwt:ClockSkew"] = "00:00:30",
                ["IdentitySeed:AdminPassword"] = IdentitySeedDefaults.KnownInsecureDemoPassword,
            })
            .Build();

        var hostEnvironment = new TestHostEnvironment(environmentName);
        services.AddSingleton<IHostEnvironment>(hostEnvironment);
        services.AddBinexusCore(configuration, hostEnvironment);
        services.AddBinexusRuntime(configuration);
        services.AddLogging();
        configure?.Invoke(services);
        return services.BuildServiceProvider().CreateScope();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Binexus.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
