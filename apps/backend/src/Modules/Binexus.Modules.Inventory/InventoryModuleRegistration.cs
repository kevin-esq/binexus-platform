using Binexus.Modules.Inventory.Application;
using Binexus.Modules.Inventory.Contracts;
using Binexus.Modules.Inventory.Features.Inventory;
using Binexus.Modules.Inventory.Infrastructure;
using Binexus.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Modules.Inventory;

public static class InventoryModuleRegistration
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddSingleton<IDbContextModelContributor, InventoryDbContextModelContributor>();
        services.AddScoped<InventoryPersistence>();
        services.AddScoped<IInventoryService, InventoryStockService>();
        services.AddScoped<IInventoryReservationApi, InventoryReservationService>();
        services.AddScoped<IInventorySaleApi, InventorySaleService>();
        return services;
    }

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints) =>
        InventoryEndpoints.MapInventoryEndpoints(endpoints);
}
