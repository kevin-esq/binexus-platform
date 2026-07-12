using Binexus.Modules.Logistics.Application;
using Binexus.Modules.Logistics.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Binexus.UnitTests.Logistics;

public sealed class LogisticsStorageOptionsTests
{
    [Fact]
    public void Production_with_local_provider_fails_validation()
    {
        var result = new LogisticsStorageOptionsValidator(new TestHostEnvironment("Production"))
            .Validate(null, new LogisticsStorageOptions { Provider = LogisticsStorageProviders.Local });

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Production_with_minio_without_credentials_fails_validation()
    {
        var result = new LogisticsStorageOptionsValidator(new TestHostEnvironment("Production"))
            .Validate(null, new LogisticsStorageOptions { Provider = LogisticsStorageProviders.MinIO });

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Development_with_local_provider_passes_validation()
    {
        var result = new LogisticsStorageOptionsValidator(new TestHostEnvironment("Development"))
            .Validate(null, new LogisticsStorageOptions { Provider = LogisticsStorageProviders.Local });

        result.Succeeded.Should().BeTrue();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Binexus.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
