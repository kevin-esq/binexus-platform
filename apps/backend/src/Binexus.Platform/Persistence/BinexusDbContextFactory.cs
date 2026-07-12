using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Binexus.Platform.Persistence;

public sealed class BinexusDbContextFactory : IDesignTimeDbContextFactory<BinexusDbContext>
{
    public BinexusDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? configuration["Database:ConnectionString"]
            ?? "Host=localhost;Port=5432;Database=binexus;Username=binexus;Password=binexus";

        var optionsBuilder = new DbContextOptionsBuilder<BinexusDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(BinexusDbContext).Assembly.FullName);
            })
            .UseSnakeCaseNamingConvention();

        var identityAssembly = Assembly.Load("Binexus.Modules.Identity");
        var contributorType = identityAssembly.GetType(
            "Binexus.Modules.Identity.Infrastructure.IdentityDbContextModelContributor",
            throwOnError: true)!;
        var contributor = (IDbContextModelContributor)Activator.CreateInstance(contributorType)!;
        var inventoryAssembly = Assembly.Load("Binexus.Modules.Inventory");
        var inventoryContributorType = inventoryAssembly.GetType(
            "Binexus.Modules.Inventory.Infrastructure.InventoryDbContextModelContributor",
            throwOnError: true)!;
        var inventoryContributor = (IDbContextModelContributor)Activator.CreateInstance(inventoryContributorType)!;
        var ordersAssembly = Assembly.Load("Binexus.Modules.Orders");
        var ordersContributorType = ordersAssembly.GetType(
            "Binexus.Modules.Orders.Infrastructure.OrdersDbContextModelContributor",
            throwOnError: true)!;
        var ordersContributor = (IDbContextModelContributor)Activator.CreateInstance(ordersContributorType)!;
        var warehouseAssembly = Assembly.Load("Binexus.Modules.Warehouse");
        var warehouseContributorType = warehouseAssembly.GetType(
            "Binexus.Modules.Warehouse.Infrastructure.WarehouseDbContextModelContributor",
            throwOnError: true)!;
        var warehouseContributor = (IDbContextModelContributor)Activator.CreateInstance(warehouseContributorType)!;
        var logisticsAssembly = Assembly.Load("Binexus.Modules.Logistics");
        var logisticsContributorType = logisticsAssembly.GetType(
            "Binexus.Modules.Logistics.Infrastructure.LogisticsDbContextModelContributor",
            throwOnError: true)!;
        var logisticsContributor = (IDbContextModelContributor)Activator.CreateInstance(logisticsContributorType)!;
        var salesAssembly = Assembly.Load("Binexus.Modules.Sales");
        var salesContributorType = salesAssembly.GetType(
            "Binexus.Modules.Sales.Infrastructure.SalesDbContextModelContributor",
            throwOnError: true)!;
        var salesContributor = (IDbContextModelContributor)Activator.CreateInstance(salesContributorType)!;

        return new BinexusDbContext(
            optionsBuilder.Options,
            new Tenancy.CurrentTenant(),
            [contributor, inventoryContributor, ordersContributor, warehouseContributor, logisticsContributor, salesContributor]);
    }
}
