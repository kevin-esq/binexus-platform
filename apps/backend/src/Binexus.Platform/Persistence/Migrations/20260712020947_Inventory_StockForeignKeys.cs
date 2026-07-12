using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_StockForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_destination_branch_id",
                table: "stock_transfers",
                column: "destination_branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_source_branch_id",
                table: "stock_transfers",
                column: "source_branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_branch_id",
                table: "stock_reservations",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_branch_id",
                table: "stock_movements",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_branch_id",
                table: "stock_items",
                column: "branch_id");

            migrationBuilder.AddForeignKey(
                name: "fk_stock_items_branches_branch_id",
                table: "stock_items",
                column: "branch_id",
                principalTable: "branches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_items_tenants_tenant_id",
                table: "stock_items",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_branches_branch_id",
                table: "stock_movements",
                column: "branch_id",
                principalTable: "branches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_tenants_tenant_id",
                table: "stock_movements",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_reservations_branches_branch_id",
                table: "stock_reservations",
                column: "branch_id",
                principalTable: "branches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_reservations_tenants_tenant_id",
                table: "stock_reservations",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfers_branches_destination_branch_id",
                table: "stock_transfers",
                column: "destination_branch_id",
                principalTable: "branches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfers_branches_source_branch_id",
                table: "stock_transfers",
                column: "source_branch_id",
                principalTable: "branches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfers_tenants_tenant_id",
                table: "stock_transfers",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stock_items_branches_branch_id",
                table: "stock_items");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_items_tenants_tenant_id",
                table: "stock_items");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_branches_branch_id",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_tenants_tenant_id",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_reservations_branches_branch_id",
                table: "stock_reservations");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_reservations_tenants_tenant_id",
                table: "stock_reservations");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfers_branches_destination_branch_id",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfers_branches_source_branch_id",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfers_tenants_tenant_id",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_destination_branch_id",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_source_branch_id",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_branch_id",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_stock_movements_branch_id",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "ix_stock_items_branch_id",
                table: "stock_items");
        }
    }
}
