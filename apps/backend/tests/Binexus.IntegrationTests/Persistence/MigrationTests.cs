using Binexus.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Persistence;

[Collection("postgres")]
public sealed class MigrationTests(PostgresTestFixture fixture) : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Migrate_on_empty_database_creates_outbox_tables()
    {
        await fixture.ApplyMigrationsAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Binexus.Platform.Persistence.BinexusDbContext>();

        var tables = await db.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('outbox_messages', 'event_handler_deliveries')
            ORDER BY table_name
            """).ToListAsync();

        tables.Should().BeEquivalentTo(["event_handler_deliveries", "outbox_messages"]);
    }

    [Fact]
    public async Task Migrate_down_and_up_roundtrip_preserves_schema()
    {
        await fixture.ApplyMigrationsAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Binexus.Platform.Persistence.BinexusDbContext>();
            await db.Database.MigrateAsync("0");
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Binexus.Platform.Persistence.BinexusDbContext>();
            var tablesAfterDown = await db.Database.SqlQuery<string>($"""
                SELECT table_name AS "Value"
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name IN ('outbox_messages', 'event_handler_deliveries')
                """).ToListAsync();
            tablesAfterDown.Should().BeEmpty();
            await db.Database.MigrateAsync();
        }

        using var verifyScope = fixture.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<Binexus.Platform.Persistence.BinexusDbContext>();
        var tables = await verifyDb.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('outbox_messages', 'event_handler_deliveries')
            ORDER BY table_name
            """).ToListAsync();
        tables.Should().BeEquivalentTo(["event_handler_deliveries", "outbox_messages"]);
    }
}
