using Binexus.Modules.Identity;
using Binexus.Modules.Inventory;
using Binexus.Modules.Inventory.Contracts;
using Binexus.Modules.Logistics;
using Binexus.Modules.Orders;
using Binexus.Modules.Orders.Contracts;
using Binexus.Modules.Sales;
using Binexus.Modules.Warehouse;
using Binexus.Platform.Features.Contracts;
using Binexus.Platform.Persistence;
using Binexus.SharedKernel.Results;
using FluentAssertions;
using NetArchTest.Rules;

namespace Binexus.ArchitectureTests.Modules;

public sealed class ModuleDependencyArchitectureTests
{
    private static readonly System.Reflection.Assembly SharedKernel =
        typeof(Result).Assembly;
    private static readonly System.Reflection.Assembly Platform =
        typeof(BinexusDbContext).Assembly;
    private static readonly System.Reflection.Assembly FeaturesContracts =
        typeof(ITenantFeatureService).Assembly;
    private static readonly System.Reflection.Assembly Identity =
        typeof(IdentityModuleRegistration).Assembly;
    private static readonly System.Reflection.Assembly InventoryContracts =
        typeof(IInventoryReservationApi).Assembly;
    private static readonly System.Reflection.Assembly Inventory =
        typeof(InventoryModuleRegistration).Assembly;
    private static readonly System.Reflection.Assembly OrdersContracts =
        typeof(IOrderFulfillmentApi).Assembly;
    private static readonly System.Reflection.Assembly Orders =
        typeof(OrdersModuleRegistration).Assembly;
    private static readonly System.Reflection.Assembly Warehouse =
        typeof(WarehouseModuleRegistration).Assembly;
    private static readonly System.Reflection.Assembly Logistics =
        typeof(LogisticsModuleRegistration).Assembly;
    private static readonly System.Reflection.Assembly Sales =
        typeof(SalesModuleRegistration).Assembly;

    [Fact]
    public void Modules_do_not_reference_other_module_implementations()
    {
        Types.InAssembly(Identity)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Inventory",
                "Binexus.Modules.Orders",
                "Binexus.Modules.Warehouse",
                "Binexus.Modules.Logistics",
                "Binexus.Modules.Sales")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(Inventory)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Identity",
                "Binexus.Modules.Orders",
                "Binexus.Modules.Warehouse",
                "Binexus.Modules.Logistics",
                "Binexus.Modules.Sales")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(Orders)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Identity",
                "Binexus.Modules.Inventory.Domain",
                "Binexus.Modules.Inventory.Application",
                "Binexus.Modules.Inventory.Infrastructure",
                "Binexus.Modules.Inventory.Features",
                "Binexus.Modules.Warehouse",
                "Binexus.Modules.Logistics",
                "Binexus.Modules.Sales")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(Warehouse)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Identity",
                "Binexus.Modules.Inventory",
                "Binexus.Modules.Orders.Domain",
                "Binexus.Modules.Orders.Application",
                "Binexus.Modules.Orders.Infrastructure",
                "Binexus.Modules.Orders.Features",
                "Binexus.Modules.Logistics",
                "Binexus.Modules.Sales")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(Logistics)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Identity.Domain",
                "Binexus.Modules.Identity.Application",
                "Binexus.Modules.Identity.Infrastructure",
                "Binexus.Modules.Identity.Features",
                "Binexus.Modules.Inventory",
                "Binexus.Modules.Warehouse",
                "Binexus.Modules.Orders.Domain",
                "Binexus.Modules.Orders.Application",
                "Binexus.Modules.Orders.Infrastructure",
                "Binexus.Modules.Orders.Features",
                "Binexus.Modules.Sales")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(Sales)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Identity.Domain",
                "Binexus.Modules.Identity.Application",
                "Binexus.Modules.Identity.Infrastructure",
                "Binexus.Modules.Identity.Features",
                "Binexus.Modules.Inventory.Domain",
                "Binexus.Modules.Inventory.Application",
                "Binexus.Modules.Inventory.Infrastructure",
                "Binexus.Modules.Inventory.Features",
                "Binexus.Modules.Orders",
                "Binexus.Modules.Warehouse",
                "Binexus.Modules.Logistics")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Orders_may_reference_inventory_contracts_only()
    {
        Orders.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().Contain("Binexus.Modules.Inventory.Contracts");
        Orders.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().NotContain("Binexus.Modules.Inventory");
    }

    [Fact]
    public void Warehouse_may_reference_orders_contracts_only()
    {
        Warehouse.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().Contain("Binexus.Modules.Orders.Contracts");
        Warehouse.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().NotContain("Binexus.Modules.Orders");
    }

    [Fact]
    public void Logistics_may_reference_orders_and_features_contracts_only()
    {
        var referenced = Logistics.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        referenced.Should().Contain("Binexus.Modules.Orders.Contracts");
        referenced.Should().Contain("Binexus.Platform.Features.Contracts");
        referenced.Should().NotContain("Binexus.Modules.Orders");
        referenced.Should().NotContain("Binexus.Modules.Identity");
        referenced.Should().NotContain("Binexus.Modules.Identity.Contracts");
        referenced.Should().NotContain("Binexus.Modules.Identity.Infrastructure");
    }

    [Fact]
    public void Sales_may_reference_inventory_and_features_contracts_only()
    {
        var referenced = Sales.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        referenced.Should().Contain("Binexus.Modules.Inventory.Contracts");
        referenced.Should().Contain("Binexus.Platform.Features.Contracts");
        referenced.Should().NotContain("Binexus.Modules.Inventory");
        referenced.Should().NotContain("Binexus.Modules.Identity");
        referenced.Should().NotContain("Binexus.Modules.Identity.Contracts");
        referenced.Should().NotContain("Binexus.Modules.Identity.Infrastructure");
        referenced.Should().NotContain("Binexus.Modules.Orders");
        referenced.Should().NotContain("Binexus.Modules.Warehouse");
    }

    [Fact]
    public void Platform_does_not_reference_any_module()
    {
        Types.InAssembly(Platform)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Identity",
                "Binexus.Modules.Inventory",
                "Binexus.Modules.Orders",
                "Binexus.Modules.Warehouse",
                "Binexus.Modules.Logistics",
                "Binexus.Modules.Sales")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Features_contracts_assembly_is_dependency_free()
    {
        FeaturesContracts.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && n.StartsWith("Binexus", StringComparison.Ordinal))
            .Should().BeEmpty();

        typeof(ITenantFeatureService).Assembly.Should().BeSameAs(FeaturesContracts);
        typeof(FeatureKey).Assembly.Should().BeSameAs(FeaturesContracts);
    }

    [Fact]
    public void SharedKernel_has_no_module_contracts_or_platform_dependencies()
    {
        Types.InAssembly(SharedKernel)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Platform",
                "Binexus.Platform.Features.Contracts",
                "Binexus.Modules.Identity",
                "Binexus.Modules.Inventory",
                "Binexus.Modules.Orders",
                "Binexus.Modules.Warehouse")
            .GetResult().IsSuccessful.Should().BeTrue();

        SharedKernel.GetTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns is not null)
            .Should().NotContain(ns =>
                ns!.Contains("Inventory", StringComparison.Ordinal)
                || ns.Contains("Orders", StringComparison.Ordinal)
                || ns.Contains("Warehouse", StringComparison.Ordinal)
                || ns.Contains("Logistics", StringComparison.Ordinal)
                || ns.Contains("Sales", StringComparison.Ordinal));
    }

    [Fact]
    public void Inventory_contracts_assembly_is_dependency_free()
    {
        InventoryContracts.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && n.StartsWith("Binexus", StringComparison.Ordinal))
            .Should().BeEmpty();

        typeof(IInventoryReservationApi).Assembly
            .Should().BeSameAs(InventoryContracts);
        typeof(IInventorySaleApi).Assembly
            .Should().BeSameAs(InventoryContracts);
    }

    [Fact]
    public void Orders_contracts_assembly_is_dependency_free()
    {
        OrdersContracts.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && n.StartsWith("Binexus", StringComparison.Ordinal))
            .Should().BeEmpty();

        typeof(IOrderFulfillmentApi).Assembly
            .Should().BeSameAs(OrdersContracts);
    }
}
