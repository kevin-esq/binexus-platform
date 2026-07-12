using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_TransferOperationKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "operation_key",
                table: "stock_transfers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_id_operation_key",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "operation_key" },
                unique: true,
                filter: "operation_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_tenant_id_operation_key",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "operation_key",
                table: "stock_transfers");
        }
    }
}
