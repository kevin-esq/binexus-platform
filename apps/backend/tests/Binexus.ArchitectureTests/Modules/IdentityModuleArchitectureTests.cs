using System.Reflection;
using Binexus.Modules.Identity;
using Binexus.Platform.Persistence;
using Binexus.SharedKernel.Results;
using FluentAssertions;
using NetArchTest.Rules;

namespace Binexus.ArchitectureTests.Modules;

public sealed class IdentityModuleArchitectureTests
{
    private static readonly Assembly IdentityAssembly = typeof(IdentityModuleRegistration).Assembly;
    private static readonly Assembly PlatformAssembly = typeof(BinexusDbContext).Assembly;
    private static readonly Assembly SharedKernelAssembly = typeof(Result).Assembly;

    [Fact]
    public void Domain_does_not_reference_infrastructure_or_platform()
    {
        var result = Types.InAssembly(IdentityAssembly)
            .That().ResideInNamespace("Binexus.Modules.Identity.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Identity.Infrastructure",
                "Binexus.Modules.Identity.Application",
                "Binexus.Modules.Identity.Features",
                "Binexus.Platform",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypes ?? []));
    }

    [Fact]
    public void Application_does_not_reference_infrastructure()
    {
        var result = Types.InAssembly(IdentityAssembly)
            .That().ResideInNamespace("Binexus.Modules.Identity.Application")
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Identity.Infrastructure",
                "Binexus.Platform",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypes ?? []));
    }

    [Fact]
    public void SharedKernel_does_not_reference_platform_or_modules()
    {
        var result = Types.InAssembly(SharedKernelAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Platform",
                "Binexus.Modules.Identity",
                "Binexus.Modules.Inventory",
                "Binexus.Modules.Orders",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypes ?? []));
    }

    [Fact]
    public void Platform_does_not_reference_modules()
    {
        var result = Types.InAssembly(PlatformAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "Binexus.Modules.Identity",
                "Binexus.Modules.Inventory",
                "Binexus.Modules.Orders")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypes ?? []));
    }
}
