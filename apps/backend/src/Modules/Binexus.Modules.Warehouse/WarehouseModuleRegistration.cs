using Binexus.Modules.Warehouse.Application;
using Binexus.Modules.Warehouse.Features.Warehouse;
using Binexus.Modules.Warehouse.Infrastructure;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Modules.Warehouse;

public static class WarehouseModuleRegistration
{
    public static IServiceCollection AddWarehouseModule(this IServiceCollection services)
    {
        services.AddSingleton<IDbContextModelContributor, WarehouseDbContextModelContributor>();
        services.AddScoped<IWarehouseQueryService, WarehouseQueryService>();
        services.AddScoped<ICommandHandler<CompletePickingTaskCommand>, CompletePickingTaskHandler>();
        services.AddScoped<IIntegrationEventProcessor, OrderApprovedWarehouseProcessor>();
        return services;
    }

    public static IEndpointRouteBuilder MapWarehouseEndpoints(this IEndpointRouteBuilder endpoints) =>
        WarehouseEndpoints.MapWarehouseEndpoints(endpoints);
}
