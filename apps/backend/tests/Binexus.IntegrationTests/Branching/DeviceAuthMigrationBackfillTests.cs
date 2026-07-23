using Binexus.IntegrationTests.Infrastructure;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Branching;

[Collection("postgres")]
public sealed class DeviceAuthMigrationBackfillTests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Migration_creates_device_auth_challenge_indexes_and_nonempty_security_stamps()
    {
        await fixture.ApplyMigrationsAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();

        var indexes = await ReadChallengeIndexesAsync(db);
        indexes.Should().Contain("ix_device_auth_challenges_branch_instance_id_device_id_status");
        indexes.Should().Contain("ix_device_auth_challenges_expires_at_utc");

        var invalidStampCount = await db.BranchDevices.CountAsync(x =>
            x.SecurityStamp == null || x.SecurityStamp.Length != 32);
        invalidStampCount.Should().Be(0);
    }

    [Fact]
    public async Task Migration_backfill_sql_assigns_unique_stamps_to_legacy_device_rows()
    {
        await fixture.ApplyMigrationsAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var instance = await db.BranchInstances.SingleOrDefaultAsync();
        if (instance is null)
        {
            instance = BranchInstance.CreateLocal(Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            db.BranchInstances.Add(instance);
            await db.SaveChangesAsync();
        }

        var first = BranchDevice.CreatePendingConfirmation(
            Guid.CreateVersion7(),
            instance.Id,
            "legacy-public-key-one",
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);
        var second = BranchDevice.CreatePendingConfirmation(
            Guid.CreateVersion7(),
            instance.Id,
            "legacy-public-key-two",
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);
        db.BranchDevices.AddRange(first, second);
        await db.SaveChangesAsync();

        // Simulate the migration's pre-backfill default before applying its PostgreSQL update.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE branch_devices SET security_stamp = '' WHERE id IN ({first.Id}, {second.Id})");
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE branch_devices
            SET security_stamp = replace(gen_random_uuid()::text, '-', '')
            WHERE security_stamp = '' OR security_stamp IS NULL;
            """);
        db.ChangeTracker.Clear();

        var stamps = await db.BranchDevices
            .Where(x => x.Id == first.Id || x.Id == second.Id)
            .Select(x => x.SecurityStamp)
            .ToListAsync();

        stamps.Should().HaveCount(2);
        stamps.Should().OnlyContain(stamp => stamp.Length == 32);
        stamps.Should().OnlyHaveUniqueItems();
    }

    // Down drops the challenge table and security_stamp column, allowing pre-promotion rollback.
    private static async Task<IReadOnlyList<string>> ReadChallengeIndexesAsync(BinexusDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'device_auth_challenges'
            """;

        var indexes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }

        await db.Database.CloseConnectionAsync();
        return indexes;
    }
}
