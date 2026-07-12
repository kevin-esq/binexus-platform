using Binexus.Modules.Identity;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Infrastructure;
using Binexus.Modules.Inventory;
using Binexus.Modules.Logistics;
using Binexus.Modules.Orders;
using Binexus.Modules.Sales;
using Binexus.Modules.Warehouse;
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
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("binexus_test")
        .WithUsername("binexus")
        .WithPassword("binexus")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ApplyMigrationsAsync();
        using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentIdentitySeeder>().SeedAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

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
        services.AddBinexusPlatform(configuration);
        services.AddBinexusDispatching();
        services.AddIdentityModule(configuration, hostEnvironment);
        services.AddInventoryModule();
        services.AddOrdersModule();
        services.AddWarehouseModule();
        services.AddLogisticsModule(configuration);
        services.AddSalesModule();
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
