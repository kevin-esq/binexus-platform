using Binexus.Modules.Orders.Application;
using Binexus.Modules.Orders.Contracts;
using Binexus.Modules.Orders.Infrastructure;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.Modules.Orders;

public static class OrdersModuleRegistration
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        services.AddSingleton<IDbContextModelContributor, OrdersDbContextModelContributor>();
        services.AddScoped<IOrdersQueryService, OrdersQueryService>();
        services.AddScoped<IOrderFulfillmentApi, OrderFulfillmentService>();
        services.AddScoped<ICommandHandler<CreateOrderCommand>, CreateOrderHandler>();
        services.AddScoped<ICommandHandler<ApproveOrderCommand>, ApproveOrderHandler>();
        services.AddScoped<ICommandHandler<CancelOrderCommand>, CancelOrderHandler>();
        services.AddScoped<ICommandHandler<RequeueFailedDeliveryOrderCommand>, RequeueFailedDeliveryOrderHandler>();
        services.AddScoped<OrderLifecycleHandlers>();
        services.AddScoped<ICommandHandler<MoveOrderToPickingCommand>>(x => x.GetRequiredService<OrderLifecycleHandlers>());
        services.AddScoped<ICommandHandler<MarkOrderReadyForDeliveryRouteCommand>>(x => x.GetRequiredService<OrderLifecycleHandlers>());
        services.AddScoped<ICommandHandler<MarkOrderOutForDeliveryCommand>>(x => x.GetRequiredService<OrderLifecycleHandlers>());
        services.AddScoped<ICommandHandler<MarkOrderDeliveredCommand>>(x => x.GetRequiredService<OrderLifecycleHandlers>());
        services.AddScoped<ICommandHandler<MarkOrderDeliveryAttemptFailedCommand>>(x => x.GetRequiredService<OrderLifecycleHandlers>());
        services.AddScoped<ICommandHandler<SettleOrderCommand>>(x => x.GetRequiredService<OrderLifecycleHandlers>());
        return services;
    }

    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints) =>
        Features.Orders.OrdersEndpoints.MapOrdersEndpoints(endpoints);
}
