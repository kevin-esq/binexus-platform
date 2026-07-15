using Binexus.Platform.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Binexus.Platform.DependencyInjection;

public static class CloudRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddCloudRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IRuntimeDescriptor, CloudRuntimeDescriptor>();
        return services;
    }
}
