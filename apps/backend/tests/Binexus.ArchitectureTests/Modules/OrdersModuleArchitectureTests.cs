using System.Reflection;
using Binexus.Modules.Orders;
using Binexus.Modules.Orders.Domain;
using Binexus.Modules.Orders.Infrastructure;
using FluentAssertions;
using NetArchTest.Rules;

namespace Binexus.ArchitectureTests.Modules;

public sealed class OrdersModuleArchitectureTests
{
    private static readonly Assembly OrdersAssembly = typeof(OrdersModuleRegistration).Assembly;

    [Fact]
    public void Domain_is_pure_and_features_do_not_reference_dbcontext()
    {
        Types.InAssembly(OrdersAssembly)
            .That().ResideInNamespace("Binexus.Modules.Orders.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Orders.Infrastructure",
                "Binexus.Modules.Orders.Application",
                "Binexus.Modules.Orders.Features",
                "Binexus.Platform",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(OrdersAssembly)
            .That().ResideInNamespace("Binexus.Modules.Orders.Features")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Binexus.Platform.Persistence")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Inventory_is_accessed_only_through_contracts_assembly()
    {
        Types.InAssembly(OrdersAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Inventory.Infrastructure",
                "Binexus.Modules.Inventory.Domain",
                "Binexus.Modules.Inventory.Features",
                "Binexus.Modules.Inventory.Application",
                "Binexus.Modules.Warehouse",
                "Binexus.Modules.Logistics",
                "Binexus.SharedKernel.Inventory")
            .GetResult().IsSuccessful.Should().BeTrue();

        typeof(ApproveOrderHandler)
            .GetConstructors()[0]
            .GetParameters()
            .Select(p => p.ParameterType.Namespace)
            .Should().Contain("Binexus.Modules.Inventory.Contracts");
    }

    [Fact]
    public void Order_transition_is_append_only()
    {
        var type = typeof(OrderTransition);
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.SetMethod?.IsPublic == true)
            .Should().BeEmpty();
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && !m.IsConstructor)
            .Should().BeEmpty();
    }
}
