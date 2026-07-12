using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Warehouse_CloseAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE picking_tasks
                SET created_from_event_id = id
                WHERE created_from_event_id IS NULL
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_from_event_id",
                table: "picking_tasks",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "completion_operation_key",
                table: "picking_tasks",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "by_user_id",
                table: "order_transitions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "causation_id",
                table: "order_transitions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "order_transitions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_picking_tasks_tenant_id_completion_operation_key",
                table: "picking_tasks",
                columns: new[] { "tenant_id", "completion_operation_key" },
                unique: true,
                filter: "completion_operation_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_picking_tasks_tenant_id_created_from_event_id",
                table: "picking_tasks",
                columns: new[] { "tenant_id", "created_from_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_picking_tasks_tenant_id_completion_operation_key",
                table: "picking_tasks");

            migrationBuilder.DropIndex(
                name: "ix_picking_tasks_tenant_id_created_from_event_id",
                table: "picking_tasks");

            migrationBuilder.DropColumn(
                name: "completion_operation_key",
                table: "picking_tasks");

            migrationBuilder.DropColumn(
                name: "causation_id",
                table: "order_transitions");

            migrationBuilder.DropColumn(
                name: "source",
                table: "order_transitions");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_from_event_id",
                table: "picking_tasks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "by_user_id",
                table: "order_transitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
