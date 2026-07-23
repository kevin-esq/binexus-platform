using Binexus.Platform.Branching.Application;
using Binexus.Platform.Branching.Client;
using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Credentials;
using Binexus.Platform.Branching.DeviceAuth;
using Binexus.Platform.Branching.Pairing;
using Binexus.Platform.Runtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        services.AddSingleton<IValidateOptions<DevicePairingOptions>, DevicePairingOptionsValidator>();
        var pairingOptions = services.AddOptions<DevicePairingOptions>()
            .Bind(configuration.GetSection(DevicePairingOptions.SectionName))
            .ValidateDataAnnotations();
        if (!BinexusRuntimeServiceCollectionExtensions.IsOpenApiDocumentGenerationHost())
        {
            pairingOptions.ValidateOnStart();
        }

        services.TryAddSingleton<IPairingReceiptVault, InMemoryPairingReceiptVault>();
        services.TryAddScoped<IBranchDevicePairingService, BranchDevicePairingService>();
        services.TryAddScoped<IBranchDeviceAdminService, BranchDeviceAdminService>();

        RegisterDeviceAuth(services, configuration);

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

    private static void RegisterDeviceAuth(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<IValidateOptions<BranchDeviceAuthOptions>, BranchDeviceAuthOptionsValidator>();
        var deviceAuth = services.AddOptions<BranchDeviceAuthOptions>()
            .Bind(configuration.GetSection(BranchDeviceAuthOptions.SectionName))
            .ValidateDataAnnotations();
        if (!BinexusRuntimeServiceCollectionExtensions.IsOpenApiDocumentGenerationHost())
        {
            deviceAuth.ValidateOnStart();
        }

        services.TryAddSingleton<ICurrentDevice, CurrentDevice>();
        services.TryAddSingleton<ICurrentTerminal, CurrentTerminal>();
        services.TryAddSingleton<IDeviceAccessTokenIssuer, DeviceAccessTokenIssuer>();
        services.TryAddSingleton<IDeviceAccessTokenValidator, DeviceAccessTokenValidator>();
        services.TryAddScoped<IDeviceStatusResolver, DeviceStatusResolver>();
        services.TryAddScoped<IBranchDeviceAuthService, BranchDeviceAuthService>();
        services.AddSingleton<IAuthorizationHandler, BranchDeviceAndUserHandler>();
        services.AddSingleton<IAuthorizationHandler, BranchDeviceOnlyHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, BranchDeviceAuthAuthorizationResultHandler>();

        services.AddAuthentication()
            .AddScheme<DeviceAccessTokenAuthenticationOptions, DeviceAccessTokenAuthenticationHandler>(
                DeviceAuthCryptoFormats.AuthenticationScheme,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                DeviceAuthCryptoFormats.DeviceAndUserPolicy,
                policy =>
                {
                    policy.AddAuthenticationSchemes(
                        JwtBearerDefaults.AuthenticationScheme,
                        DeviceAuthCryptoFormats.AuthenticationScheme);
                    policy.Requirements.Add(new BranchDeviceAndUserRequirement());
                });
            options.AddPolicy(
                DeviceAuthCryptoFormats.DeviceOnlyPolicy,
                policy =>
                {
                    policy.AddAuthenticationSchemes(DeviceAuthCryptoFormats.AuthenticationScheme);
                    policy.Requirements.Add(new BranchDeviceOnlyRequirement());
                });
        });

        services.AddSingleton<IHostedService, BranchDeviceAuthBootWarningService>();
    }
}

file sealed class BranchDeviceAuthBootWarningService(
    IOptions<BranchDeviceAuthOptions> options,
    IHostEnvironment environment,
    ILogger<BranchDeviceAuthBootWarningService> logger) : IHostedService
{
    private static readonly Action<ILogger, bool, Exception?> LogInsecure =
        LoggerMessage.Define<bool>(
            LogLevel.Warning,
            new EventId(2100, "InsecureBranchTransport"),
            "Branch device auth: insecure HTTP transport permitted (AllowInsecureBranchTransport={Allow}). HTTP LAN is not a supported production configuration.");

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.AllowInsecureBranchTransport
            || environment.IsDevelopment()
            || environment.IsEnvironment("Testing"))
        {
            LogInsecure(logger, options.Value.AllowInsecureBranchTransport, null);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
