using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Orders_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    total_cents = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_currency_iso3", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_orders_state", "state IN ('DRAFT','APPROVED','PICKING','READY_FOR_DELIVERY_ROUTE','OUT_FOR_DELIVERY','DELIVERY_ATTEMPT_FAILED','DELIVERED','SETTLED','CANCELLED')");
                    table.CheckConstraint("ck_orders_total_cents_non_negative", "total_cents >= 0");
                    table.ForeignKey(
                        name: "fk_orders_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    product_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_cents = table.Column<int>(type: "integer", nullable: false),
                    line_total_cents = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => x.id);
                    table.CheckConstraint("ck_order_lines_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_order_lines_total_non_negative", "line_total_cents >= 0");
                    table.CheckConstraint("ck_order_lines_unit_price_non_negative", "unit_price_cents >= 0");
                    table.ForeignKey(
                        name: "fk_order_lines_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_transitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_transitions_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_order_id_id",
                table: "order_lines",
                columns: new[] { "order_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_order_transitions_operation_key",
                table: "order_transitions",
                column: "operation_key",
                unique: true,
                filter: "operation_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_order_transitions_order_id_occurred_at_utc_id",
                table: "order_transitions",
                columns: new[] { "order_id", "occurred_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_branch_id",
                table: "orders",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_branch_id_state",
                table: "orders",
                columns: new[] { "tenant_id", "branch_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_created_at_utc_id",
                table: "orders",
                columns: new[] { "tenant_id", "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_operation_key",
                table: "orders",
                columns: new[] { "tenant_id", "operation_key" },
                unique: true,
                filter: "operation_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_lines");

            migrationBuilder.DropTable(
                name: "order_transitions");

            migrationBuilder.DropTable(
                name: "orders");
        }
    }
}
