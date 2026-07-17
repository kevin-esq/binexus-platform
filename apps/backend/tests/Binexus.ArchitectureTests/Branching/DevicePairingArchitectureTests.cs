using System.Reflection;
using Binexus.Modules.Identity;
using Binexus.Modules.Inventory;
using Binexus.Modules.Logistics;
using Binexus.Modules.Orders;
using Binexus.Modules.Sales;
using Binexus.Modules.Warehouse;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Pairing;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.DependencyInjection;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;

namespace Binexus.ArchitectureTests.Branching;

public sealed class DevicePairingArchitectureTests
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

    private static IConfiguration EmptyConfiguration { get; } =
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    [Fact]
    public void Modules_do_not_reference_pairing_namespaces()
    {
        foreach (var module in Modules)
        {
            foreach (var forbidden in new[]
            {
                "Binexus.Platform.Branching.Pairing",
                "Binexus.Platform.Branching.Persistence",
                "Binexus.Platform.Branching.Application",
                "Binexus.Platform.Branching.Crypto",
            })
            {
                Types.InAssembly(module)
                    .ShouldNot()
                    .HaveDependencyOn(forbidden)
                    .GetResult()
                    .IsSuccessful.Should().BeTrue(because: $"{module.GetName().Name} must not depend on {forbidden}");
            }
        }
    }

    [Fact]
    public void Sales_module_does_not_reference_branch_device_or_terminal_types()
    {
        var salesTypes = Types.InAssembly(typeof(SalesModuleRegistration).Assembly)
            .That().HaveDependencyOnAny(
                typeof(BranchDevice).FullName!,
                typeof(BranchTerminal).FullName!,
                typeof(DevicePairingRequest).FullName!)
            .GetTypes();

        salesTypes.Should().BeEmpty();
    }

    [Fact]
    public void Pairing_types_live_in_platform()
    {
        typeof(BranchDevice).Assembly.Should().BeSameAs(Platform);
        typeof(IBranchDevicePairingService).Assembly.Should().BeSameAs(Platform);
        typeof(IBranchDeviceAdminService).Assembly.Should().BeSameAs(Platform);
        typeof(IPairingReceiptVault).Assembly.Should().BeSameAs(Platform);
    }

    [Fact]
    public void Branch_runtime_registers_pairing_services()
    {
        var services = new ServiceCollection();
        services.AddBranchRuntime(EmptyConfiguration);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IBranchDevicePairingService)
            && d.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(d =>
            d.ServiceType == typeof(IBranchDeviceAdminService)
            && d.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(d =>
            d.ServiceType == typeof(IPairingReceiptVault)
            && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Cloud_runtime_does_not_register_pairing_services()
    {
        var services = new ServiceCollection();
        services.AddCloudRuntime(EmptyConfiguration);
        using var provider = services.BuildServiceProvider();

        provider.GetService<IBranchDevicePairingService>().Should().BeNull();
        provider.GetService<IBranchDeviceAdminService>().Should().BeNull();
        provider.GetService<IPairingReceiptVault>().Should().BeNull();
    }

    [Fact]
    public void Pairing_wire_contracts_do_not_expose_persisted_secrets()
    {
        var forbiddenOnResponses = new HashSet<string>(StringComparer.Ordinal)
        {
            "DeviceCredential",
            "PrivateKey",
            "PairingReceiptHash",
            "StatusTokenHash",
            "CredentialHash",
        };

        var pairingResponses = typeof(PairingExchangeResponse).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Binexus.Platform.Branching.Contracts"
                && t.IsClass
                && t.Name.EndsWith("Response", StringComparison.Ordinal))
            .ToList();

        foreach (var type in pairingResponses)
        {
            foreach (var property in type.GetProperties())
            {
                forbiddenOnResponses.Should().NotContain(property.Name, because: $"{type.Name} must not expose {property.Name}");
                if (type.Name is nameof(PairedDeviceResponse))
                {
                    property.Name.Should().NotBe("PublicKey", because: "admin listings expose fingerprint only");
                }
            }
        }

        typeof(PairingExchangeRequest).GetProperties().Select(p => p.Name)
            .Should().NotContain("DeviceCredential");
        typeof(PairingConfirmRequest).GetProperties().Select(p => p.Name)
            .Should().NotContain("PairingReceiptHash");
        typeof(PairingStatusResponse).GetProperties().Select(p => p.Name)
            .Should().Contain("PairingReceipt")
            .And.NotContain("PairingReceiptHash");
    }
}
