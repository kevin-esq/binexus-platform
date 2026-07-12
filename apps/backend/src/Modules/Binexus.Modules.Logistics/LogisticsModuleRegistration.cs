using Binexus.Modules.Logistics.Application;
using Binexus.Modules.Logistics.Features.Logistics;
using Binexus.Modules.Logistics.Infrastructure;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Binexus.Modules.Logistics;

public static class LogisticsModuleRegistration
{
    public static IServiceCollection AddLogisticsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<LogisticsStorageOptions>, LogisticsStorageOptionsValidator>();
        services.AddOptions<LogisticsStorageOptions>()
            .Bind(configuration.GetSection(LogisticsStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<LogisticsFeatureOptions>()
            .Bind(configuration.GetSection(LogisticsFeatureOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IDbContextModelContributor, LogisticsDbContextModelContributor>();
        services.AddScoped<ILogisticsQueryService, LogisticsQueryService>();
        services.AddSingleton<IObjectStorage>(sp =>
        {
            var storageOptions = sp.GetRequiredService<IOptions<LogisticsStorageOptions>>().Value;
            if (storageOptions.IsLocal)
            {
                return ActivatorUtilities.CreateInstance<LocalObjectStorage>(sp);
            }

            return ActivatorUtilities.CreateInstance<MinioObjectStorage>(sp);
        });
        services.AddScoped<ILogisticsProofUploadService, LogisticsProofUploadService>();
        services.AddScoped<ILogisticsProofObjectVerifier, LogisticsProofObjectVerifier>();
        services.AddScoped<ICommandHandler<CreateDeliveryRouteCommand>, CreateDeliveryRouteHandler>();
        services.AddScoped<ICommandHandler<AssignOrdersToDeliveryRouteCommand>, AssignOrdersToDeliveryRouteHandler>();
        services.AddScoped<ICommandHandler<DispatchDeliveryRouteCommand>, DispatchDeliveryRouteHandler>();
        services.AddScoped<ICommandHandler<ConfirmDeliveryCommand>, ConfirmDeliveryHandler>();
        services.AddScoped<ICommandHandler<ReportFailedDeliveryCommand>, ReportFailedDeliveryHandler>();
        services.AddScoped<ICommandHandler<LiquidateDeliveryRouteCommand>, LiquidateDeliveryRouteHandler>();
        services.AddScoped<IIntegrationEventProcessor, OrderReadyForDeliveryRouteLogisticsProcessor>();
        services.AddScoped<IIntegrationEventProcessor, OrderCancelledLogisticsProcessor>();
        return services;
    }

    public static IEndpointRouteBuilder MapLogisticsEndpoints(this IEndpointRouteBuilder endpoints) =>
        LogisticsEndpoints.MapLogisticsEndpoints(endpoints);

    public static IEndpointRouteBuilder MapLogisticsDevStorageEndpoints(this IEndpointRouteBuilder endpoints) =>
        LogisticsDevStorageEndpoints.MapLogisticsDevStorageEndpoints(endpoints);
}
