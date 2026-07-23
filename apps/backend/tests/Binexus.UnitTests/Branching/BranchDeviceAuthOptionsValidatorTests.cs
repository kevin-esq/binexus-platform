using Binexus.Platform.Branching.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Binexus.UnitTests.Branching;

public sealed class BranchDeviceAuthOptionsValidatorTests
{
    [Fact]
    public void Missing_current_kid_is_rejected()
    {
        var options = CreateOptions();
        options.CurrentKeyId = string.Empty;

        Validate(options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Unknown_current_kid_is_rejected()
    {
        var options = CreateOptions();
        options.CurrentKeyId = "retired";

        Validate(options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Weak_key_is_rejected()
    {
        Validate(CreateOptions("short")).Failed.Should().BeTrue();
    }

    [Fact]
    public void Duplicate_kid_is_rejected()
    {
        var options = CreateOptions();
        options.SigningKeys.Add(new BranchDeviceAuthSigningKey
        {
            KeyId = "current",
            Key = "another-integration-test-device-auth-key-32",
        });

        Validate(options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Empty_key_ring_is_rejected()
    {
        var options = CreateOptions();
        options.SigningKeys = [];

        Validate(options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Jwt_signing_key_collision_is_rejected()
    {
        var options = CreateOptions();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = options.SigningKeys[0].Key,
            })
            .Build();

        new BranchDeviceAuthOptionsValidator(new TestEnvironment(), configuration)
            .Validate(null, options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Jwt_signing_key_collision_uses_utf8_key_bytes()
    {
        var key = string.Concat(Enumerable.Repeat("🔐", 8));
        var options = CreateOptions(key);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = key,
            })
            .Build();

        new BranchDeviceAuthOptionsValidator(new TestEnvironment(), configuration)
            .Validate(null, options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Lab_only_current_key_is_rejected_in_production()
    {
        var options = CreateOptions();
        options.SigningKeys[0].LabOnly = true;

        Validate(options, "Production").Failed.Should().BeTrue();
    }

    [Fact]
    public void Development_only_current_key_is_rejected_in_production()
    {
        var options = CreateOptions("development-only-device-auth-key-32");

        Validate(options, "Production").Failed.Should().BeTrue();
    }

    [Fact]
    public void Lab_only_current_key_is_accepted_in_testing()
    {
        var options = CreateOptions();
        options.SigningKeys[0].LabOnly = true;

        Validate(options, "Testing").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Valid_rotation_ring_is_accepted()
    {
        var options = CreateOptions();
        options.SigningKeys.Add(new BranchDeviceAuthSigningKey
        {
            KeyId = "previous",
            Key = "previous-integration-test-device-auth-key-32",
        });

        Validate(options).Succeeded.Should().BeTrue();
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(
        BranchDeviceAuthOptions options,
        string environmentName = "Testing") =>
        new BranchDeviceAuthOptionsValidator(
            new TestEnvironment { EnvironmentName = environmentName },
            new ConfigurationBuilder().Build()).Validate(null, options);

    private static BranchDeviceAuthOptions CreateOptions(string key = "current-integration-test-device-auth-key-32") =>
        new()
        {
            CurrentKeyId = "current",
            SigningKeys =
            [
                new BranchDeviceAuthSigningKey { KeyId = "current", Key = key },
            ],
        };

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
