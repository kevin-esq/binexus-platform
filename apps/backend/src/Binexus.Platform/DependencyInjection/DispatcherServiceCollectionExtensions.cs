using Binexus.Platform.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Platform.DependencyInjection;

public static class DispatcherServiceCollectionExtensions
{
    public static IServiceCollection AddBinexusDispatching(this IServiceCollection services)
    {
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        return services;
    }
}
