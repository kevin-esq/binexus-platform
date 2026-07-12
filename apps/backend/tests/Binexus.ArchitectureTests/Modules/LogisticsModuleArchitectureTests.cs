using System.Reflection;
using Binexus.Modules.Logistics;
using Binexus.Modules.Logistics.Domain;
using Binexus.Modules.Logistics.Infrastructure;
using Binexus.Modules.Orders;
using Binexus.Platform.Messaging;
using FluentAssertions;
using NetArchTest.Rules;

namespace Binexus.ArchitectureTests.Modules;

public sealed class LogisticsModuleArchitectureTests
{
    private static readonly Assembly LogisticsAssembly = typeof(LogisticsModuleRegistration).Assembly;
    private static readonly Assembly OrdersAssembly = typeof(OrdersModuleRegistration).Assembly;

    [Fact]
    public void Domain_is_pure_and_features_do_not_reference_dbcontext()
    {
        Types.InAssembly(LogisticsAssembly)
            .That().ResideInNamespace("Binexus.Modules.Logistics.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Logistics.Infrastructure",
                "Binexus.Modules.Logistics.Application",
                "Binexus.Modules.Logistics.Features",
                "Binexus.Platform",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(LogisticsAssembly)
            .That().ResideInNamespace("Binexus.Modules.Logistics.Features")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Binexus.Platform.Persistence")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Orders_is_accessed_only_through_contracts_assembly()
    {
        Types.InAssembly(LogisticsAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Orders.Infrastructure",
                "Binexus.Modules.Orders.Domain",
                "Binexus.Modules.Orders.Features",
                "Binexus.Modules.Orders.Application")
            .GetResult().IsSuccessful.Should().BeTrue();

        typeof(DispatchDeliveryRouteHandler)
            .GetConstructors()[0]
            .GetParameters()
            .Select(p => p.ParameterType.Namespace)
            .Should().Contain("Binexus.Modules.Orders.Contracts");
    }

    [Fact]
    public void Orders_does_not_consume_delivery_events()
    {
        var processorEventNames = OrdersAssembly.GetTypes()
            .Where(type => typeof(IIntegrationEventProcessor).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type => (IIntegrationEventProcessor)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type))
            .Select(processor => processor.EventName);

        processorEventNames.Should().NotContain([
            "DELIVERY_ROUTE_DISPATCHED",
            "DELIVERY_CONFIRMED",
            "DELIVERY_FAILED",
            "DELIVERY_ROUTE_LIQUIDATED",
        ]);
    }

    [Fact]
    public void Reserved_cancellation_and_skip_transitions_have_no_public_writers()
    {
        typeof(DeliveryRoute).GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public).Should().BeNull();
        typeof(DeliveryRouteStop).GetMethod("Skip", BindingFlags.Instance | BindingFlags.Public).Should().BeNull();
    }
}
