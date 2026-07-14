using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Identity.Infrastructure;
using Binexus.Platform.Features.Contracts;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Binexus.IntegrationTests.Identity;

public sealed class IdentitySeedEnvironmentTests
{
    [Fact]
    public void Production_rejects_known_insecure_demo_password_configuration()
    {
        var action = () => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.UseSetting("Database:ConnectionString",
                    "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
                builder.UseSetting("Binexus:RuntimeMode", "Cloud");
                builder.UseSetting(
                    "Jwt:SigningKey",
                    "production-test-signing-key-with-more-than-thirty-two-bytes");
                builder.UseSetting("Logistics:Storage:Provider", "MinIO");
                builder.UseSetting("Logistics:Storage:Endpoint", "http://127.0.0.1:9000");
                builder.UseSetting("Logistics:Storage:Bucket", "binexus-test");
                builder.UseSetting("Logistics:Storage:AccessKey", "test-access-key");
                builder.UseSetting("Logistics:Storage:SecretKey", "test-secret-key");
                builder.UseSetting(
                    "IdentitySeed:AdminPassword",
                    IdentitySeedDefaults.KnownInsecureDemoPassword);
            })
            .CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*known insecure demo password*");
    }

    [Fact]
    public void Production_rejects_known_insecure_local_signing_key()
    {
        var action = () => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.UseSetting("Database:ConnectionString",
                    "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
                builder.UseSetting("Binexus:RuntimeMode", "Cloud");
                builder.UseSetting(
                    "Jwt:SigningKey",
                    IdentitySeedDefaults.KnownInsecureLocalSigningKey);
                builder.UseSetting("Logistics:Storage:Provider", "MinIO");
                builder.UseSetting("Logistics:Storage:Endpoint", "http://127.0.0.1:9000");
                builder.UseSetting("Logistics:Storage:Bucket", "binexus-test");
                builder.UseSetting("Logistics:Storage:AccessKey", "test-access-key");
                builder.UseSetting("Logistics:Storage:SecretKey", "test-secret-key");
            })
            .CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*DEVELOPMENT-ONLY*");
    }

    [Fact]
    public void Staging_rejects_known_insecure_local_signing_key()
    {
        var action = () => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Staging");
                builder.UseSetting("Database:ConnectionString",
                    "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
                builder.UseSetting("Binexus:RuntimeMode", "Cloud");
                builder.UseSetting(
                    "Jwt:SigningKey",
                    IdentitySeedDefaults.KnownInsecureLocalSigningKey);
                builder.UseSetting("Logistics:Storage:Provider", "MinIO");
                builder.UseSetting("Logistics:Storage:Endpoint", "http://127.0.0.1:9000");
                builder.UseSetting("Logistics:Storage:Bucket", "binexus-test");
                builder.UseSetting("Logistics:Storage:AccessKey", "test-access-key");
                builder.UseSetting("Logistics:Storage:SecretKey", "test-secret-key");
            })
            .CreateClient();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*DEVELOPMENT-ONLY*");
    }

    [Fact]
    public void Development_allows_known_insecure_local_signing_key()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("Database:ConnectionString",
                "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
            builder.UseSetting("Binexus:RuntimeMode", "Cloud");
            builder.UseSetting(
                "Jwt:SigningKey",
                IdentitySeedDefaults.KnownInsecureLocalSigningKey);
            builder.UseSetting("Logistics:Storage:Provider", "MinIO");
            builder.UseSetting("Logistics:Storage:Endpoint", "http://127.0.0.1:9000");
            builder.UseSetting("Logistics:Storage:Bucket", "binexus-test");
            builder.UseSetting("Logistics:Storage:AccessKey", "test-access-key");
            builder.UseSetting("Logistics:Storage:SecretKey", "test-secret-key");
        });

        using var client = factory.CreateClient();
        client.Should().NotBeNull();
    }

    [Fact]
    public void Api_publish_output_does_not_embed_known_insecure_local_signing_key()
    {
        var backendRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var publishDirs = new[]
        {
            Path.Combine(backendRoot, "src", "Binexus.Api", "bin"),
            Path.Combine(backendRoot, "src", "Binexus.Workers", "bin"),
        };

        foreach (var dir in publishDirs.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "appsettings*.json", SearchOption.AllDirectories))
            {
                File.ReadAllText(file).Should().NotContain(
                    IdentitySeedDefaults.KnownInsecureLocalSigningKey,
                    because: file);
            }
        }
    }

    [Fact]
    public void Production_does_not_register_demo_seed()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("Database:ConnectionString",
                "Host=localhost;Port=5432;Database=binexus_test;Username=binexus;Password=binexus");
            builder.UseSetting("Binexus:RuntimeMode", "Cloud");
            builder.UseSetting(
                "Jwt:SigningKey",
                "production-test-signing-key-with-more-than-thirty-two-bytes");
            builder.UseSetting("Logistics:Storage:Provider", "MinIO");
            builder.UseSetting("Logistics:Storage:Endpoint", "http://127.0.0.1:9000");
            builder.UseSetting("Logistics:Storage:Bucket", "binexus-test");
            builder.UseSetting("Logistics:Storage:AccessKey", "test-access-key");
            builder.UseSetting("Logistics:Storage:SecretKey", "test-secret-key");
        });

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetService<DevelopmentIdentitySeeder>().Should().BeNull();
    }

    [Fact]
    public void Api_configuration_files_do_not_embed_known_demo_password()
    {
        var backendRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var configFiles = Directory.EnumerateFiles(
                Path.Combine(backendRoot, "src", "Binexus.Api"),
                "appsettings*.json",
                SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(backendRoot, "src", "Binexus.Api", "bin"),
                "appsettings*.json",
                SearchOption.AllDirectories));

        foreach (var file in configFiles)
        {
            File.ReadAllText(file).Should().NotContain(
                IdentitySeedDefaults.KnownInsecureDemoPassword,
                because: file);
        }
    }
}

[Collection("postgres")]
public sealed class IdentitySeedFeatureFlagTests : IClassFixture<PostgresTestFixture>
{
    private readonly PostgresTestFixture _postgres;

    public IdentitySeedFeatureFlagTests(PostgresTestFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Testing_seed_leaves_pos_retail_and_liquidation_disabled()
    {
        using var scope = _postgres.CreateScope(environmentName: "Testing");
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenantId = await db.Set<Tenant>()
            .IgnoreQueryFilters()
            .Where(x => x.Slug == "acme")
            .Select(x => x.Id)
            .SingleAsync();

        var features = await db.Set<TenantFeature>()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId
                && (x.Key == FeatureKeyValues.PosRetail || x.Key == FeatureKeyValues.Liquidation))
            .ToListAsync();

        features.Should().HaveCount(2);
        features.Should().OnlyContain(x => !x.Enabled);
    }

    [Fact]
    public async Task Development_seed_enables_pos_retail_and_liquidation()
    {
        using var scope = _postgres.CreateScope(environmentName: "Development");
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentIdentitySeeder>();
        await seeder.SeedAsync();

        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenantId = await db.Set<Tenant>()
            .IgnoreQueryFilters()
            .Where(x => x.Slug == "acme")
            .Select(x => x.Id)
            .SingleAsync();

        var features = await db.Set<TenantFeature>()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId
                && (x.Key == FeatureKeyValues.PosRetail || x.Key == FeatureKeyValues.Liquidation))
            .ToListAsync();

        features.Should().HaveCount(2);
        features.Should().OnlyContain(x => x.Enabled);

        // Restore Testing defaults so other fixtures sharing the container stay deterministic.
        var now = DateTimeOffset.UtcNow;
        foreach (var feature in features)
        {
            feature.SetEnabled(false, now);
        }

        await db.SaveChangesAsync();
    }
}
