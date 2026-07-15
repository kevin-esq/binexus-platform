using Binexus.Platform.Branching.Application;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Binexus.Platform.DependencyInjection;

public static class BranchRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddBranchRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IRuntimeDescriptor, BranchRuntimeDescriptor>();
        services.TryAddSingleton<BranchInstanceMemoryStore>();
        services.TryAddScoped<IBranchInstanceInitializer, BranchInstanceInitializer>();
        services.TryAddSingleton<IBranchInstanceAccessor, BranchInstanceAccessor>();
        return services;
    }
}
