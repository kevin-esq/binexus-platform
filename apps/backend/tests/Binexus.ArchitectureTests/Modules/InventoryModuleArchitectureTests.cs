using System.Reflection;
using Binexus.Modules.Inventory;
using Binexus.Modules.Inventory.Domain;
using Binexus.Platform.Persistence;
using Binexus.SharedKernel.Results;
using FluentAssertions;
using NetArchTest.Rules;

namespace Binexus.ArchitectureTests.Modules;

public sealed class InventoryModuleArchitectureTests
{
    private static readonly Assembly InventoryAssembly =
        typeof(InventoryModuleRegistration).Assembly;
    private static readonly Assembly PlatformAssembly =
        typeof(BinexusDbContext).Assembly;
    private static readonly Assembly SharedKernelAssembly =
        typeof(Result).Assembly;

    [Fact]
    public void Domain_does_not_reference_infrastructure_or_platform()
    {
        var result = Types.InAssembly(InventoryAssembly)
            .That().ResideInNamespace("Binexus.Modules.Inventory.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Inventory.Infrastructure",
                "Binexus.Modules.Inventory.Application",
                "Binexus.Modules.Inventory.Features",
                "Binexus.Platform",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypes ?? []));
    }

    [Fact]
    public void Application_does_not_reference_infrastructure()
    {
        var result = Types.InAssembly(InventoryAssembly)
            .That().ResideInNamespace("Binexus.Modules.Inventory.Application")
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Inventory.Infrastructure",
                "Binexus.Platform",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypes ?? []));
    }

    [Fact]
    public void Features_do_not_reference_dbcontext()
    {
        var result = Types.InAssembly(InventoryAssembly)
            .That().ResideInNamespace("Binexus.Modules.Inventory.Features")
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Binexus.Platform.Persistence")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypes ?? []));
    }

    [Fact]
    public void Platform_and_shared_kernel_do_not_reference_inventory()
    {
        Types.InAssembly(PlatformAssembly)
            .ShouldNot().HaveDependencyOn("Binexus.Modules.Inventory")
            .GetResult()
            .IsSuccessful.Should().BeTrue();

        Types.InAssembly(SharedKernelAssembly)
            .ShouldNot().HaveDependencyOn("Binexus.Modules.Inventory")
            .GetResult()
            .IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Stock_movement_is_append_only()
    {
        var movementType = typeof(StockMovement);
        var writableProperties = movementType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => property.Name);
        var publicMethods = movementType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && !method.IsConstructor)
            .Select(method => method.Name);

        writableProperties.Should().BeEmpty();
        publicMethods.Should().BeEmpty();
    }
}
