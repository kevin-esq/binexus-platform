using System.Reflection;
using Binexus.Modules.Identity;
using Binexus.Modules.Inventory;
using Binexus.Modules.Logistics;
using Binexus.Modules.Orders;
using Binexus.Modules.Sales;
using Binexus.Modules.Warehouse;
using Binexus.Platform.Persistence;
using Binexus.Platform.Runtime;
using FluentAssertions;
using NetArchTest.Rules;

namespace Binexus.ArchitectureTests.Runtime;

public sealed class RuntimeCompositionArchitectureTests
{
    private static readonly Assembly Platform = typeof(BinexusDbContext).Assembly;
    private static readonly Assembly[] Modules =
    [
        typeof(IdentityModuleRegistration).Assembly,
        typeof(InventoryModuleRegistration).Assembly,
        typeof(OrdersModuleRegistration).Assembly,
        typeof(WarehouseModuleRegistration).Assembly,
        typeof(LogisticsModuleRegistration).Assembly,
        typeof(SalesModuleRegistration).Assembly,
    ];

    [Fact]
    public void Modules_do_not_use_RuntimeMode_types()
    {
        foreach (var module in Modules)
        {
            var result = Types.InAssembly(module)
                .ShouldNot()
                .HaveDependencyOn("Binexus.Platform.Runtime")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.GetName().Name} must not depend on Binexus.Platform.Runtime. Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Fact]
    public void Platform_does_not_reference_Composition_or_Modules()
    {
        Types.InAssembly(Platform)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Binexus.Composition",
                "Binexus.Modules.Identity",
                "Binexus.Modules.Inventory",
                "Binexus.Modules.Orders",
                "Binexus.Modules.Warehouse",
                "Binexus.Modules.Logistics",
                "Binexus.Modules.Sales")
            .GetResult()
            .IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void RuntimeMode_enum_lives_in_Platform()
    {
        typeof(RuntimeMode).Assembly.Should().BeSameAs(Platform);
        typeof(IRuntimeDescriptor).Assembly.Should().BeSameAs(Platform);
    }
}
