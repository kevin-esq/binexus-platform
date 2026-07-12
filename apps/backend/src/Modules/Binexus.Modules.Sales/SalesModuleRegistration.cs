using Binexus.Modules.Sales.Application;
using Binexus.Modules.Sales.Features.Sales;
using Binexus.Modules.Sales.Infrastructure;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Modules.Sales;

public static class SalesModuleRegistration
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services)
    {
        services.AddSingleton<IDbContextModelContributor, SalesDbContextModelContributor>();
        services.AddScoped<SalesFeatureGate>();
        services.AddScoped<ISalesQueryService, SalesQueryService>();
        services.AddScoped<ICommandHandler<OpenSalesSessionCommand>, OpenSalesSessionHandler>();
        services.AddScoped<ICommandHandler<CreateSaleCommand>, CreateSaleHandler>();
        services.AddScoped<ICommandHandler<CloseSalesSessionCommand>, CloseSalesSessionHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder endpoints) =>
        SalesEndpoints.MapSalesEndpoints(endpoints);
}
