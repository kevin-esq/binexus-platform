using Binexus.Modules.Identity;
using Binexus.Modules.Inventory;
using Binexus.Modules.Logistics;
using Binexus.Modules.Orders;
using Binexus.Modules.Sales;
using Binexus.Modules.Warehouse;
using Binexus.Platform.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Binexus.Composition;

/// <summary>
/// Shared persistence, tenancy, dispatching, modules, and outbox processor service.
/// Does not register HTTP presentation, OpenAPI, CORS middleware, or hosted outbox workers.
/// Lives outside Platform so Platform never references Modules.
/// </summary>
public static class BinexusCoreServiceCollectionExtensions
{
    public static IServiceCollection AddBinexusCore(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddBinexusPlatform(configuration);
        services.AddBinexusDispatching();
        services.AddIdentityModule(configuration, environment);
        services.AddInventoryModule();
        services.AddOrdersModule();
        services.AddWarehouseModule();
        services.AddLogisticsModule(configuration);
        services.AddSalesModule();
        return services;
    }
}
