using Binexus.IntegrationTests.Infrastructure;
using FluentAssertions;
using Npgsql;

namespace Binexus.IntegrationTests.Persistence;

[Collection("postgres")]
public sealed class SchemaCatalogTests(PostgresTestFixture fixture) : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Database_schema_matches_expected_outbox_contract()
    {
        await fixture.ApplyMigrationsAsync();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        var columnTypes = await QueryStringsAsync(connection, """
            SELECT table_name || '.' || column_name || ':' || udt_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name IN ('outbox_messages', 'event_handler_deliveries')
            """);

        columnTypes.Should().Contain("outbox_messages.id:uuid");
        columnTypes.Should().Contain("outbox_messages.payload_json:jsonb");
        columnTypes.Should().Contain("outbox_messages.occurred_at_utc:timestamptz");
        columnTypes.Should().Contain("event_handler_deliveries.handler_key:varchar");

        var inventoryTables = await QueryStringsAsync(connection, """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('stock_items','stock_reservations','stock_movements','stock_transfers')
            """);
        inventoryTables.Should().BeEquivalentTo(
            ["stock_items", "stock_reservations", "stock_movements", "stock_transfers"]);

        var orderTables = await QueryStringsAsync(connection, """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('orders','order_lines','order_transitions')
            """);
        orderTables.Should().BeEquivalentTo(["orders", "order_lines", "order_transitions"]);

        var warehouseTables = await QueryStringsAsync(connection, """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('picking_tasks','picking_lines')
            """);
        warehouseTables.Should().BeEquivalentTo(["picking_tasks", "picking_lines"]);

        var orderChecks = await QueryStringsAsync(connection, """
            SELECT conname
            FROM pg_constraint
            WHERE contype = 'c'
              AND conrelid = 'public.orders'::regclass
            """);
        orderChecks.Should().Contain(c => c.Contains("state", StringComparison.Ordinal));
        orderChecks.Should().Contain(c => c.Contains("currency", StringComparison.Ordinal));

        var stockChecks = await QueryStringsAsync(connection, """
            SELECT conname
            FROM pg_constraint
            WHERE contype = 'c'
              AND conrelid = 'public.stock_items'::regclass
            """);
        stockChecks.Should().Contain(c => c.Contains("on_hand", StringComparison.Ordinal));
        stockChecks.Should().Contain(c => c.Contains("reserved", StringComparison.Ordinal));

        var pickingLineChecks = await QueryStringsAsync(connection, """
            SELECT conname
            FROM pg_constraint
            WHERE contype = 'c'
              AND conrelid = 'public.picking_lines'::regclass
            """);
        pickingLineChecks.Should().Contain(c => c.Contains("quantity", StringComparison.Ordinal));
        pickingLineChecks.Should().Contain(c => c.Contains("picked", StringComparison.Ordinal));

        var uniqueIndexes = await QueryStringsAsync(connection, """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'event_handler_deliveries'
              AND indexdef LIKE '%UNIQUE%'
            """);

        uniqueIndexes.Should().Contain(i =>
            i.Contains("tenant_id", StringComparison.Ordinal)
            && i.Contains("event_id", StringComparison.Ordinal)
            && i.Contains("handler_key", StringComparison.Ordinal));

        var foreignKeys = await QueryStringsAsync(connection, """
            SELECT confdeltype::text
            FROM pg_constraint
            WHERE contype = 'f'
              AND conrelid = 'public.event_handler_deliveries'::regclass
            """);

        foreignKeys.Should().ContainSingle().Which.Should().Be("c");

        var indexes = await QueryStringsAsync(connection, """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN ('outbox_messages', 'event_handler_deliveries')
            """);

        indexes.Should().Contain(i => i.Contains("status", StringComparison.Ordinal) && i.Contains("locked_until", StringComparison.Ordinal));
        indexes.Should().Contain(i => i.Contains("tenant_id", StringComparison.Ordinal) && i.Contains("handler_key", StringComparison.Ordinal));

        var warehouseIndexes = await QueryStringsAsync(connection, """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN ('picking_tasks', 'picking_lines')
            """);
        warehouseIndexes.Should().Contain(i => i.Contains("tenant_id", StringComparison.Ordinal) && i.Contains("order_id", StringComparison.Ordinal) && i.Contains("UNIQUE", StringComparison.Ordinal));
        warehouseIndexes.Should().Contain(i => i.Contains("tenant_id", StringComparison.Ordinal) && i.Contains("status", StringComparison.Ordinal));
        warehouseIndexes.Should().Contain(i => i.Contains("tenant_id", StringComparison.Ordinal) && i.Contains("branch_id", StringComparison.Ordinal));

        var salesTables = await QueryStringsAsync(connection, """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('sales_sessions','sales','sale_lines','payment_captures')
            """);
        salesTables.Should().BeEquivalentTo(["sales_sessions", "sales", "sale_lines", "payment_captures"]);

        var openTerminalUnique = await QueryStringsAsync(connection, """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'ix_sales_sessions_open_terminal_unique'
            """);
        openTerminalUnique.Should().ContainSingle();
        openTerminalUnique[0].Contains("UNIQUE", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        openTerminalUnique[0].Contains("WHERE", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        openTerminalUnique[0].Contains("status", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        openTerminalUnique[0].Contains("OPEN", StringComparison.OrdinalIgnoreCase).Should().BeTrue();

        var salesFks = await QueryStringsAsync(connection, """
            SELECT conname || ':' || confdeltype::text || ':' || pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE contype = 'f'
              AND conrelid IN ('public.sales'::regclass, 'public.payment_captures'::regclass)
            """);
        salesFks.Should().Contain(f =>
            f.Contains("sales_session", StringComparison.OrdinalIgnoreCase)
            && f.Contains("tenant_id", StringComparison.OrdinalIgnoreCase)
            && f.Contains("session_id", StringComparison.OrdinalIgnoreCase)
            && f.Contains(":r:", StringComparison.Ordinal));
        salesFks.Should().Contain(f =>
            f.Contains("payment_captures", StringComparison.OrdinalIgnoreCase)
            && f.Contains("sale_id", StringComparison.OrdinalIgnoreCase)
            && f.Contains("session_id", StringComparison.OrdinalIgnoreCase)
            && f.Contains(":c:", StringComparison.Ordinal));

        var sessionSaleRestrict = await QueryStringsAsync(connection, """
            SELECT confdeltype::text
            FROM pg_constraint
            WHERE contype = 'f'
              AND conrelid = 'public.sales'::regclass
              AND confrelid = 'public.sales_sessions'::regclass
            """);
        sessionSaleRestrict.Should().ContainSingle().Which.Should().Be("r");

        var salesChecks = await QueryStringsAsync(connection, """
            SELECT conname
            FROM pg_constraint
            WHERE contype = 'c'
              AND conrelid IN ('public.sales_sessions'::regclass, 'public.sales'::regclass, 'public.payment_captures'::regclass)
            """);
        salesChecks.Should().Contain(c => c.Contains("status", StringComparison.Ordinal));
        salesChecks.Should().Contain(c => c.Contains("method", StringComparison.Ordinal));
        salesChecks.Should().Contain(c => c.Contains("opening_float", StringComparison.Ordinal));

        var xminMapped = await QueryStringsAsync(connection, """
            SELECT a.attname
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND c.relname = 'sales_sessions'
              AND a.attname = 'xmin'
            """);
        xminMapped.Should().ContainSingle();

        var operationKeyUniques = await QueryStringsAsync(connection, """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN ('sales_sessions', 'sales')
              AND indexdef LIKE '%UNIQUE%'
              AND (indexdef LIKE '%open_operation_key%' OR indexdef LIKE '%close_operation_key%' OR indexdef LIKE '%operation_key%')
            """);
        operationKeyUniques.Should().NotBeEmpty();
        operationKeyUniques.Should().Contain(i => i.Contains("open_operation_key", StringComparison.Ordinal));
        operationKeyUniques.Should().Contain(i => i.Contains("operation_key", StringComparison.Ordinal));
    }

    private static async Task<List<string>> QueryStringsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var results = new List<string>();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }
}
