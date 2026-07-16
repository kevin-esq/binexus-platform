using System.Reflection;
using Binexus.Modules.Identity;
using Binexus.Modules.Inventory;
using Binexus.Modules.Logistics;
using Binexus.Modules.Orders;
using Binexus.Modules.Sales;
using Binexus.Modules.Warehouse;
using Binexus.Platform.Branching.Application;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.DependencyInjection;
using Binexus.Platform.Persistence;
using Binexus.SharedKernel.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;

namespace Binexus.ArchitectureTests.Branching;

public sealed class BranchInstanceArchitectureTests
{
    private static readonly Assembly Platform = typeof(BinexusDbContext).Assembly;
    private static readonly Assembly SharedKernel = typeof(ITenantScoped).Assembly;
    private static readonly Assembly[] Modules =
    [
        typeof(IdentityModuleRegistration).Assembly,
        typeof(InventoryModuleRegistration).Assembly,
        typeof(OrdersModuleRegistration).Assembly,
        typeof(WarehouseModuleRegistration).Assembly,
        typeof(LogisticsModuleRegistration).Assembly,
        typeof(SalesModuleRegistration).Assembly,
    ];

    private static IConfiguration EmptyConfiguration { get; } =
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    [Fact]
    public void Modules_do_not_reference_Platform_Branching_implementation()
    {
        foreach (var module in Modules)
        {
            Types.InAssembly(module)
                .ShouldNot()
                .HaveDependencyOn("Binexus.Platform.Branching.Application")
                .GetResult()
                .IsSuccessful.Should().BeTrue(because: module.GetName().Name);

            Types.InAssembly(module)
                .ShouldNot()
                .HaveDependencyOn("Binexus.Platform.Branching.Persistence")
                .GetResult()
                .IsSuccessful.Should().BeTrue(because: module.GetName().Name);

            Types.InAssembly(module)
                .ShouldNot()
                .HaveDependencyOn("Binexus.Platform.Branching.Crypto")
                .GetResult()
                .IsSuccessful.Should().BeTrue(because: module.GetName().Name);
        }
    }

    [Fact]
    public void SharedKernel_does_not_contain_BranchInstance()
    {
        Types.InAssembly(SharedKernel)
            .That().HaveNameMatching("BranchInstance.*")
            .GetTypes()
            .Should().BeEmpty();
    }

    [Fact]
    public void Branch_identity_types_live_in_Platform()
    {
        typeof(BranchInstance).Assembly.Should().BeSameAs(Platform);
        typeof(IBranchInstanceAccessor).Assembly.Should().BeSameAs(Platform);
        typeof(BranchInstanceInitializer).Assembly.Should().BeSameAs(Platform);
    }

    [Fact]
    public void Cloud_runtime_does_not_register_branch_identity_services()
    {
        var services = new ServiceCollection();
        services.AddCloudRuntime(EmptyConfiguration);
        using var provider = services.BuildServiceProvider();

        provider.GetService<IBranchInstanceAccessor>().Should().BeNull();
        provider.GetService<IBranchInstanceInitializer>().Should().BeNull();
        provider.GetService<BranchInstanceMemoryStore>().Should().BeNull();
    }

    [Fact]
    public void Branch_runtime_registers_correct_lifetimes()
    {
        var services = new ServiceCollection();
        services.AddBranchRuntime(EmptyConfiguration);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IBranchInstanceAccessor)
            && d.ImplementationType == typeof(BranchInstanceAccessor)
            && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(d =>
            d.ServiceType == typeof(BranchInstanceMemoryStore)
            && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(d =>
            d.ServiceType == typeof(IBranchInstanceInitializer)
            && d.ImplementationType == typeof(BranchInstanceInitializer)
            && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void Memory_store_and_accessor_do_not_capture_DbContext()
    {
        typeof(BranchInstanceMemoryStore).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(f => f.FieldType)
            .Should().NotContain(typeof(BinexusDbContext));

        typeof(BranchInstanceAccessor).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(f => f.FieldType)
            .Should().NotContain(typeof(BinexusDbContext));

        typeof(BranchInstanceMemoryStore).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(f => f.FieldType)
            .Should().NotContain(typeof(BranchInstance));
    }

    [Fact]
    public void Branch_runtime_registers_accessor_and_initializer()
    {
        var services = new ServiceCollection();
        services.AddBranchRuntime(EmptyConfiguration);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IBranchInstanceAccessor)
            && d.ImplementationType == typeof(BranchInstanceAccessor));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IBranchInstanceInitializer)
            && d.ImplementationType == typeof(BranchInstanceInitializer));
        services.Should().Contain(d =>
            d.ServiceType == typeof(BranchInstanceMemoryStore));
    }
}
