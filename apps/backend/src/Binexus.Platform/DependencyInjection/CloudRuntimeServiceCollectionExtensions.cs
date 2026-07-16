using Binexus.Platform.Branching.Application;
using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.DependencyInjection;

public static class CloudRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddCloudRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IRuntimeDescriptor, CloudRuntimeDescriptor>();
        services.AddSingleton<IValidateOptions<CloudActivationOptions>, CloudActivationOptionsValidator>();
        var options = services.AddOptions<CloudActivationOptions>()
            .Bind(configuration.GetSection(CloudActivationOptions.SectionName))
            .ValidateDataAnnotations();
        if (!BinexusRuntimeServiceCollectionExtensions.IsOpenApiDocumentGenerationHost())
        {
            options.ValidateOnStart();
        }

        services.TryAddScoped<ICloudBranchActivationService, CloudBranchActivationService>();
        return services;
    }
}
