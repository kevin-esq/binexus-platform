using Binexus.Platform.Configuration;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Binexus.Platform.DependencyInjection;

public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddBinexusPlatform(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<OutboxWorkerOptions>()
            .Bind(configuration.GetSection(OutboxWorkerOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICurrentTenant, CurrentTenant>();
        services.TryAddSingleton<IIdGenerator, UuidV7IdGenerator>();
        services.TryAddSingleton<IEventHandlerRegistry, IntegrationEventHandlerRegistry>();

        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
            ?? throw new InvalidOperationException("Database configuration is required.");

        services.AddDbContext<BinexusDbContext>(options =>
            options.UseNpgsql(databaseOptions.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(BinexusDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IOutboxProcessor, OutboxProcessor>();

        return services;
    }
}
