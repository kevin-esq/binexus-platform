using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Warehouse_Picking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "picking_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_from_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_picking_tasks", x => x.id);
                    table.CheckConstraint("ck_picking_tasks_status", "status IN ('PENDING','COMPLETED','CANCELLED')");
                    table.ForeignKey(
                        name: "fk_picking_tasks_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_picking_tasks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "picking_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    picking_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    picked_quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_picking_lines", x => x.id);
                    table.CheckConstraint("ck_picking_lines_picked_non_negative", "picked_quantity >= 0");
                    table.CheckConstraint("ck_picking_lines_picked_not_above_quantity", "picked_quantity <= quantity");
                    table.CheckConstraint("ck_picking_lines_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_picking_lines_picking_task_picking_task_id",
                        column: x => x.picking_task_id,
                        principalTable: "picking_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_picking_lines_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_picking_lines_picking_task_id",
                table: "picking_lines",
                column: "picking_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_picking_lines_tenant_id_picking_task_id_order_line_id",
                table: "picking_lines",
                columns: new[] { "tenant_id", "picking_task_id", "order_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_picking_tasks_branch_id",
                table: "picking_tasks",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_picking_tasks_tenant_id_branch_id",
                table: "picking_tasks",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_picking_tasks_tenant_id_order_id",
                table: "picking_tasks",
                columns: new[] { "tenant_id", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_picking_tasks_tenant_id_status",
                table: "picking_tasks",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "picking_lines");

            migrationBuilder.DropTable(
                name: "picking_tasks");
        }
    }
}
