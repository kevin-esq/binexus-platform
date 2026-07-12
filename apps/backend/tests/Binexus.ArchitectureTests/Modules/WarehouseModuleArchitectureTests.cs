using System.Reflection;
using Binexus.Modules.Warehouse;
using Binexus.Modules.Warehouse.Infrastructure;
using Binexus.Platform.Messaging;
using FluentAssertions;
using NetArchTest.Rules;

namespace Binexus.ArchitectureTests.Modules;

public sealed class WarehouseModuleArchitectureTests
{
    private static readonly Assembly WarehouseAssembly = typeof(WarehouseModuleRegistration).Assembly;

    [Fact]
    public void Domain_is_pure_and_features_do_not_reference_dbcontext()
    {
        Types.InAssembly(WarehouseAssembly)
            .That().ResideInNamespace("Binexus.Modules.Warehouse.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Warehouse.Infrastructure",
                "Binexus.Modules.Warehouse.Application",
                "Binexus.Modules.Warehouse.Features",
                "Binexus.Platform",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(WarehouseAssembly)
            .That().ResideInNamespace("Binexus.Modules.Warehouse.Features")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Binexus.Platform.Persistence")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Orders_is_accessed_only_through_contracts_assembly()
    {
        Types.InAssembly(WarehouseAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Orders.Infrastructure",
                "Binexus.Modules.Orders.Domain",
                "Binexus.Modules.Orders.Features",
                "Binexus.Modules.Orders.Application",
                "Binexus.SharedKernel.Orders")
            .GetResult().IsSuccessful.Should().BeTrue();

        typeof(CompletePickingTaskHandler)
            .GetConstructors()[0]
            .GetParameters()
            .Select(p => p.ParameterType.Namespace)
            .Should().Contain("Binexus.Modules.Orders.Contracts");
    }

    [Fact]
    public void No_processor_consumes_picking_completed()
    {
        var processorEventNames = WarehouseAssembly.GetTypes()
            .Where(type => typeof(IIntegrationEventProcessor).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type => (IIntegrationEventProcessor)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type))
            .Select(processor => processor.EventName);

        processorEventNames.Should().NotContain("PICKING_COMPLETED");
    }
}
