using Binexus.Platform.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Binexus.Platform.DependencyInjection;

public static class BranchRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddBranchRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IRuntimeDescriptor, BranchRuntimeDescriptor>();
        return services;
    }
}
