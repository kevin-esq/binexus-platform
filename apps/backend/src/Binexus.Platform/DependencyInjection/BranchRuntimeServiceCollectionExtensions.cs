using Binexus.Platform.Branching.Application;
using Binexus.Platform.Branching.Client;
using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Credentials;
using Binexus.Platform.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Binexus.Platform.DependencyInjection;

public static class BranchRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddBranchRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IRuntimeDescriptor, BranchRuntimeDescriptor>();
        services.TryAddSingleton<BranchInstanceMemoryStore>();
        services.TryAddScoped<IBranchInstanceInitializer, BranchInstanceInitializer>();
        services.TryAddSingleton<IBranchInstanceAccessor, BranchInstanceAccessor>();
        services.TryAddScoped<IBranchActivationOrchestrator, BranchActivationOrchestrator>();

        services.AddOptions<BranchCloudClientOptions>()
            .Bind(configuration.GetSection(BranchCloudClientOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                "BranchCloud:BaseUrl must be absolute.")
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<BranchCredentialStoreOptions>, BranchCredentialStoreOptionsValidator>();
        services.AddOptions<BranchCredentialStoreOptions>()
            .Bind(configuration.GetSection(BranchCredentialStoreOptions.SectionName))
            .ValidateOnStart();

        services.AddHttpClient<ICloudActivationClient, CloudActivationHttpClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<BranchCloudClientOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.Timeout = options.Timeout;
            });

        services.AddSingleton<IBranchCredentialStore>(sp =>
        {
            var environment = sp.GetRequiredService<IHostEnvironment>();
            var options = sp.GetRequiredService<IOptions<BranchCredentialStoreOptions>>().Value;
            if (environment.IsEnvironment("Testing") || options.Provider == "InMemory")
            {
                return new InMemoryBranchCredentialStore();
            }

            if (environment.IsDevelopment()
                && (options.Provider is "DevelopmentFile" or "None"))
            {
                return new DevelopmentFileBranchCredentialStore(environment);
            }

            throw new InvalidOperationException(
                $"BranchCredentialStore provider '{options.Provider}' is not available.");
        });

        return services;
    }
}
