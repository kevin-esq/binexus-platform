using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Binexus.Composition;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Platform.Branching.Application;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.DependencyInjection;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Binexus.IntegrationTests.Branching;

[Collection("postgres")]
public sealed class BranchInstanceIntegrationTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task First_ensure_creates_row_second_ensure_returns_same_id_without_update()
    {
        await TruncateBranchInstancesAsync();

        await using var scope1 = CreateBranchScope();
        var first = await scope1.ServiceProvider
            .GetRequiredService<IBranchInstanceInitializer>()
            .EnsureAsync();

        await using var scope2 = CreateBranchScope();
        var second = await scope2.ServiceProvider
            .GetRequiredService<IBranchInstanceInitializer>()
            .EnsureAsync();

        second.Id.Should().Be(first.Id);
        second.Status.Should().Be(BranchServerStatus.ReadyForActivation);

        await using var verify = CreateBranchScope();
        var count = await verify.ServiceProvider.GetRequiredService<BinexusDbContext>()
            .BranchInstances.CountAsync();
        count.Should().Be(1);

        var row = await verify.ServiceProvider.GetRequiredService<BinexusDbContext>()
            .BranchInstances.AsNoTracking().SingleAsync();
        row.SingletonKey.Should().Be(BranchInstance.LocalSingletonKey);
        row.Status.Should().Be(BranchInstance.ReadyForActivationStatus);
        row.Id.Version.Should().Be(7);
    }

    [Fact]
    public async Task Concurrent_ensure_produces_single_row()
    {
        await TruncateBranchInstancesAsync();

        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = CreateBranchScope();
            return await scope.ServiceProvider
                .GetRequiredService<IBranchInstanceInitializer>()
                .EnsureAsync();
        });

        var results = await Task.WhenAll(tasks);
        results.Select(r => r.Id).Distinct().Should().ContainSingle();

        await using var verify = CreateBranchScope();
        (await verify.ServiceProvider.GetRequiredService<BinexusDbContext>().BranchInstances.CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task Singleton_key_other_than_local_violates_check()
    {
        await TruncateBranchInstancesAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO branch_instances (id, singleton_key, status, created_at_utc)
            VALUES (gen_random_uuid(), 'other', 'ReadyForActivation', NOW())
            """,
            connection);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Second_local_row_violates_unique()
    {
        await TruncateBranchInstancesAsync();
        await using var scope = CreateBranchScope();
        await scope.ServiceProvider.GetRequiredService<IBranchInstanceInitializer>().EnsureAsync();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO branch_instances (id, singleton_key, status, created_at_utc)
            VALUES (gen_random_uuid(), 'local', 'ReadyForActivation', NOW())
            """,
            connection);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Cloud_startup_leaves_zero_branch_instance_rows()
    {
        await TruncateBranchInstancesAsync();

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Cloud");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
        });

        _ = factory.CreateClient();

        using var scope = fixture.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<BinexusDbContext>().BranchInstances.CountAsync())
            .Should().Be(0);

        factory.Services.GetService<IBranchInstanceAccessor>().Should().BeNull();
        factory.Services.GetService<IBranchInstanceInitializer>().Should().BeNull();
    }

    [Fact]
    public async Task Branch_health_returns_200_after_init_Cloud_returns_404()
    {
        await TruncateBranchInstancesAsync();

        await using var branchFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Branch");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
        });

        var branchClient = branchFactory.CreateClient();
        var branchResponse = await branchClient.GetAsync("/health/branch");
        branchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await branchResponse.Content.ReadFromJsonAsync<BranchHealthDto>();
        body!.Status.Should().Be("ReadyForActivation");
        Guid.Parse(body.BranchInstanceId).Version.Should().Be(7);

        await using var cloudFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Binexus:RuntimeMode", "Cloud");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
        });

        var cloudResponse = await cloudFactory.CreateClient().GetAsync("/health/branch");
        cloudResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Restart_preserves_branch_instance_id()
    {
        await TruncateBranchInstancesAsync();

        Guid firstId;
        await using (var factory1 = new WebApplicationFactory<Program>().WithWebHostBuilder(ConfigureBranch))
        {
            var body = await (await factory1.CreateClient().GetAsync("/health/branch"))
                .Content.ReadFromJsonAsync<BranchHealthDto>();
            firstId = Guid.Parse(body!.BranchInstanceId);
        }

        await using (var factory2 = new WebApplicationFactory<Program>().WithWebHostBuilder(ConfigureBranch))
        {
            var body = await (await factory2.CreateClient().GetAsync("/health/branch"))
                .Content.ReadFromJsonAsync<BranchHealthDto>();
            Guid.Parse(body!.BranchInstanceId).Should().Be(firstId);
        }

        void ConfigureBranch(IWebHostBuilder builder)
        {
            builder.UseSetting("Binexus:RuntimeMode", "Branch");
            builder.UseSetting("Database:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-more-than-32-bytes");
            builder.UseSetting("SEED_ON_START", "0");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
        }
    }

    [Fact]
    public async Task Schema_has_expected_columns_and_no_secret_fields()
    {
        await fixture.ApplyMigrationsAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'branch_instances'
            ORDER BY column_name
            """,
            connection);
        var columns = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        // User columns only — xmin is a PostgreSQL system column (not in information_schema.columns).
        columns.Should().BeEquivalentTo(["created_at_utc", "id", "singleton_key", "status"]);
        columns.Should().NotContain(c =>
            c.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || c.Contains("token", StringComparison.OrdinalIgnoreCase)
            || c.Contains("password", StringComparison.OrdinalIgnoreCase)
            || c.Contains("tenant", StringComparison.OrdinalIgnoreCase)
            || c.Contains("branch_id", StringComparison.OrdinalIgnoreCase)
            || c.Contains("activated", StringComparison.OrdinalIgnoreCase)
            || c.Contains("display", StringComparison.OrdinalIgnoreCase)
            || c.Contains("last_started", StringComparison.OrdinalIgnoreCase));

        await using var xminCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*)::int
            FROM pg_attribute a
            JOIN pg_class c ON a.attrelid = c.oid
            JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE n.nspname = 'public'
              AND c.relname = 'branch_instances'
              AND a.attname = 'xmin'
              AND NOT a.attisdropped
            """,
            connection);
        Convert.ToInt32(await xminCmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(1);
    }

    [Fact]
    public async Task Catalog_has_unique_and_check_constraints_on_singleton_key()
    {
        await fixture.ApplyMigrationsAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var indexCmd = new NpgsqlCommand(
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'branch_instances'
              AND indexname = 'ix_branch_instances_singleton_key'
            """,
            connection);
        (await indexCmd.ExecuteScalarAsync()).Should().Be(BranchInstance.SingletonKeyUniqueIndexName);

        await using var checksCmd = new NpgsqlCommand(
            """
            SELECT conname
            FROM pg_constraint
            WHERE contype = 'c'
              AND conrelid = 'public.branch_instances'::regclass
            ORDER BY conname
            """,
            connection);
        var checks = new List<string>();
        await using (var reader = await checksCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                checks.Add(reader.GetString(0));
            }
        }

        checks.Should().Contain("ck_branch_instances_singleton_key_local");
        checks.Should().Contain("ck_branch_instances_status_ready_for_activation");

        await using var nullCmd = new NpgsqlCommand(
            """
            SELECT is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'branch_instances'
              AND column_name = 'singleton_key'
            """,
            connection);
        (await nullCmd.ExecuteScalarAsync()).Should().Be("NO");
    }

    [Fact]
    public async Task Check_violation_on_singleton_key_is_not_treated_as_ensure_success()
    {
        await TruncateBranchInstancesAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO branch_instances (id, singleton_key, status, created_at_utc)
            VALUES (gen_random_uuid(), 'other', 'ReadyForActivation', NOW())
            """,
            connection);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        ex.Which.ConstraintName.Should().Be("ck_branch_instances_singleton_key_local");
        BranchInstancePostgresErrors.IsExpectedSingletonRace(ex.Which.SqlState, ex.Which.ConstraintName)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Ensure_cancellation_propagates_and_does_not_publish()
    {
        await TruncateBranchInstancesAsync();
        await using var scope = CreateBranchScope();
        var store = scope.ServiceProvider.GetRequiredService<BranchInstanceMemoryStore>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await scope.ServiceProvider
            .GetRequiredService<IBranchInstanceInitializer>()
            .EnsureAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        store.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task Ensure_with_unreachable_database_fails_without_publishing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] =
                    "Host=127.0.0.1;Port=1;Database=missing;Username=binexus;Password=binexus;Timeout=1",
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                ["Binexus:RuntimeMode"] = "Branch",
                ["Jwt:Issuer"] = "binexus",
                ["Jwt:Audience"] = "binexus-api",
                ["Jwt:SigningKey"] = "integration-test-signing-key-with-more-than-32-bytes",
                ["Jwt:AccessTokenDuration"] = "00:15:00",
                ["Jwt:RefreshTokenDuration"] = "7.00:00:00",
                ["Jwt:ClockSkew"] = "00:00:30",
            })
            .Build();
        var env = new BranchTestHostEnvironment();
        services.AddSingleton<IHostEnvironment>(env);
        services.AddBinexusCore(configuration, env);
        services.AddBinexusRuntime(configuration);
        services.AddLogging();
        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<BranchInstanceMemoryStore>();

        var act = async () => await scope.ServiceProvider
            .GetRequiredService<IBranchInstanceInitializer>()
            .EnsureAsync();

        await act.Should().ThrowAsync<Exception>();
        store.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task Independent_process_scopes_share_same_persisted_id_without_row_mutation()
    {
        await TruncateBranchInstancesAsync();

        await using var processA = CreateBranchScope();
        var idA = (await processA.ServiceProvider.GetRequiredService<IBranchInstanceInitializer>().EnsureAsync()).Id;
        var createdAt = (await processA.ServiceProvider.GetRequiredService<BinexusDbContext>()
            .BranchInstances.AsNoTracking().SingleAsync()).CreatedAtUtc;

        await using var processB = CreateBranchScope();
        var idB = (await processB.ServiceProvider.GetRequiredService<IBranchInstanceInitializer>().EnsureAsync()).Id;
        idB.Should().Be(idA);

        await using var verify = CreateBranchScope();
        var row = await verify.ServiceProvider.GetRequiredService<BinexusDbContext>()
            .BranchInstances.AsNoTracking().SingleAsync();
        row.CreatedAtUtc.Should().Be(createdAt);
        (await verify.ServiceProvider.GetRequiredService<BinexusDbContext>().BranchInstances.CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task Second_ensure_in_same_process_does_not_replace_published_identity()
    {
        await TruncateBranchInstancesAsync();
        await using var scope = CreateBranchScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IBranchInstanceInitializer>();
        var accessor = scope.ServiceProvider.GetRequiredService<IBranchInstanceAccessor>();

        var first = await initializer.EnsureAsync();
        var published = await accessor.GetAsync();
        published.Should().Be(first);

        var second = await initializer.EnsureAsync();
        second.Should().Be(first);
        (await accessor.GetAsync()).Should().Be(published);
    }

    [Fact]
    public async Task Migration_down_removes_branch_instances_then_up_restores()
    {
        await fixture.ApplyMigrationsAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();

        await db.GetService<IMigrator>()!
            .MigrateAsync("20260712072341_Sales_ClosingAdjustments");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var existsCmd = new NpgsqlCommand(
                         """
                         SELECT COUNT(*)::int
                         FROM information_schema.tables
                         WHERE table_schema = 'public' AND table_name = 'branch_instances'
                         """,
                         connection))
        {
            Convert.ToInt32(await existsCmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)
                .Should().Be(0);
        }

        await db.Database.MigrateAsync();
        await using (var existsCmd = new NpgsqlCommand(
                         """
                         SELECT COUNT(*)::int
                         FROM information_schema.tables
                         WHERE table_schema = 'public' AND table_name = 'branch_instances'
                         """,
                         connection))
        {
            Convert.ToInt32(await existsCmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)
                .Should().Be(1);
        }
    }

    private AsyncServiceScope CreateBranchScope()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = fixture.ConnectionString,
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                ["Binexus:RuntimeMode"] = "Branch",
                ["Jwt:Issuer"] = "binexus",
                ["Jwt:Audience"] = "binexus-api",
                ["Jwt:SigningKey"] = "integration-test-signing-key-with-more-than-32-bytes",
                ["Jwt:AccessTokenDuration"] = "00:15:00",
                ["Jwt:RefreshTokenDuration"] = "7.00:00:00",
                ["Jwt:ClockSkew"] = "00:00:30",
            })
            .Build();

        var env = new BranchTestHostEnvironment();
        services.AddSingleton<IHostEnvironment>(env);
        services.AddBinexusCore(configuration, env);
        services.AddBinexusRuntime(configuration);
        services.AddLogging();
        return services.BuildServiceProvider().CreateAsyncScope();
    }

    private async Task TruncateBranchInstancesAsync()
    {
        await fixture.ApplyMigrationsAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE branch_instances");
    }

    private sealed class BranchTestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Binexus.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed record BranchHealthDto(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("branchInstanceId")] string BranchInstanceId);
}
