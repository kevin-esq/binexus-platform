using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Orders_TransitionTenantCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_order_transitions_operation_key",
                table: "order_transitions");

            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "order_transitions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "order_transitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_order_transitions_tenant_id_operation_key",
                table: "order_transitions",
                columns: new[] { "tenant_id", "operation_key" },
                unique: true,
                filter: "operation_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_order_transitions_tenant_id_order_id_occurred_at_utc",
                table: "order_transitions",
                columns: new[] { "tenant_id", "order_id", "occurred_at_utc" });

            migrationBuilder.AddForeignKey(
                name: "fk_order_transitions_tenants_tenant_id",
                table: "order_transitions",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_order_transitions_tenants_tenant_id",
                table: "order_transitions");

            migrationBuilder.DropIndex(
                name: "ix_order_transitions_tenant_id_operation_key",
                table: "order_transitions");

            migrationBuilder.DropIndex(
                name: "ix_order_transitions_tenant_id_order_id_occurred_at_utc",
                table: "order_transitions");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "order_transitions");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "order_transitions");

            migrationBuilder.CreateIndex(
                name: "ix_order_transitions_operation_key",
                table: "order_transitions",
                column: "operation_key",
                unique: true,
                filter: "operation_key IS NOT NULL");
        }
    }
}
