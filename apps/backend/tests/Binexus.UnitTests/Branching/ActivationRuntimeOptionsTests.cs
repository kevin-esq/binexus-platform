using Binexus.Platform.Branching.Configuration;
using Binexus.Platform.Branching.Credentials;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Binexus.UnitTests.Branching;

public sealed class ActivationRuntimeOptionsTests
{
    [Fact]
    public void Cloud_production_rejects_missing_and_known_development_pepper()
    {
        var prod = new FakeEnvironment { EnvironmentName = Environments.Production };
        var validator = new CloudActivationOptionsValidator(prod);

        validator.Validate(null, new CloudActivationOptions { CodePepper = "" })
            .Failed.Should().BeTrue();
        validator.Validate(
                null,
                new CloudActivationOptions { CodePepper = CloudActivationOptions.KnownDevelopmentPepper })
            .Failed.Should().BeTrue();
        validator.Validate(
                null,
                new CloudActivationOptions { CodePepper = "production-grade-cloud-activation-pepper-value" })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Cloud_development_allows_known_development_pepper()
    {
        var validator = new CloudActivationOptionsValidator(new FakeEnvironment { EnvironmentName = Environments.Development });
        validator.Validate(
                null,
                new CloudActivationOptions { CodePepper = CloudActivationOptions.KnownDevelopmentPepper })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Branch_production_rejects_any_credential_store_provider()
    {
        var validator = new BranchCredentialStoreOptionsValidator(
            new FakeEnvironment { EnvironmentName = Environments.Production });
        validator.Validate(null, new BranchCredentialStoreOptions { Provider = "DevelopmentFile" })
            .Failed.Should().BeTrue();
        validator.Validate(null, new BranchCredentialStoreOptions { Provider = "InMemory" })
            .Failed.Should().BeTrue();
        validator.Validate(null, new BranchCredentialStoreOptions { Provider = "None" })
            .Failed.Should().BeTrue();
    }

    [Fact]
    public void Branch_testing_allows_in_memory_rejects_development_file()
    {
        var validator = new BranchCredentialStoreOptionsValidator(
            new FakeEnvironment { EnvironmentName = "Testing" });
        validator.Validate(null, new BranchCredentialStoreOptions { Provider = "InMemory" })
            .Succeeded.Should().BeTrue();
        validator.Validate(null, new BranchCredentialStoreOptions { Provider = "DevelopmentFile" })
            .Failed.Should().BeTrue();
    }

    [Fact]
    public void Branch_development_allows_development_file()
    {
        var validator = new BranchCredentialStoreOptionsValidator(
            new FakeEnvironment { EnvironmentName = Environments.Development });
        validator.Validate(null, new BranchCredentialStoreOptions { Provider = "DevelopmentFile" })
            .Succeeded.Should().BeTrue();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
