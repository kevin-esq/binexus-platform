using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Logistics_DeliveryRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_route_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_from_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_route_candidates", x => x.id);
                    table.CheckConstraint("ck_delivery_route_candidates_status", "status IN ('READY','ASSIGNED','CANCELLED')");
                    table.ForeignKey(
                        name: "fk_delivery_route_candidates_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_delivery_route_candidates_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_route_liquidations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_cents = table.Column<int>(type: "integer", nullable: false),
                    declared_cents = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    discrepancy_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    liquidated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_route_liquidations", x => x.id);
                    table.CheckConstraint("ck_delivery_route_liquidations_currency_iso3", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_delivery_route_liquidations_declared_non_negative", "declared_cents >= 0");
                    table.CheckConstraint("ck_delivery_route_liquidations_expected_non_negative", "expected_cents >= 0");
                    table.ForeignKey(
                        name: "fk_delivery_route_liquidations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_routes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    driver_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planned_date = table.Column<DateOnly>(type: "date", nullable: true),
                    dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creation_operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    assign_operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    dispatch_operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    completion_operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_routes", x => x.id);
                    table.CheckConstraint("ck_delivery_routes_status", "status IN ('PLANNED','DISPATCHED','COMPLETED','CANCELLED')");
                    table.ForeignKey(
                        name: "fk_delivery_routes_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_delivery_routes_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_route_liquidation_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_route_liquidation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_route_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_cents = table.Column<int>(type: "integer", nullable: false),
                    declared_cents = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_route_liquidation_lines", x => x.id);
                    table.CheckConstraint("ck_delivery_route_liquidation_lines_declared_non_negative", "declared_cents >= 0");
                    table.CheckConstraint("ck_delivery_route_liquidation_lines_expected_non_negative", "expected_cents >= 0");
                    table.ForeignKey(
                        name: "fk_delivery_route_liquidation_lines_delivery_route_liquidation",
                        column: x => x.delivery_route_liquidation_id,
                        principalTable: "delivery_route_liquidations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_delivery_route_liquidation_lines_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_route_stops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    failure_notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completion_operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    failure_operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_route_stops", x => x.id);
                    table.CheckConstraint("ck_delivery_route_stops_sequence_positive", "sequence > 0");
                    table.CheckConstraint("ck_delivery_route_stops_status", "status IN ('PLANNED','DELIVERED','FAILED','SKIPPED')");
                    table.ForeignKey(
                        name: "fk_delivery_route_stops_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_delivery_route_stops_delivery_routes_delivery_route_id",
                        column: x => x.delivery_route_id,
                        principalTable: "delivery_routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_delivery_route_stops_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_proofs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_route_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    photo_object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    signature_object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    recipient = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_proofs", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_proofs_delivery_route_stop_delivery_route_stop_id",
                        column: x => x.delivery_route_stop_id,
                        principalTable: "delivery_route_stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_delivery_proofs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_proofs_delivery_route_stop_id",
                table: "delivery_proofs",
                column: "delivery_route_stop_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_proofs_tenant_id_delivery_route_stop_id",
                table: "delivery_proofs",
                columns: new[] { "tenant_id", "delivery_route_stop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_candidates_branch_id",
                table: "delivery_route_candidates",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_candidates_tenant_id_branch_id_status",
                table: "delivery_route_candidates",
                columns: new[] { "tenant_id", "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_candidates_tenant_id_created_from_event_id",
                table: "delivery_route_candidates",
                columns: new[] { "tenant_id", "created_from_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_candidates_tenant_id_order_id",
                table: "delivery_route_candidates",
                columns: new[] { "tenant_id", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_liquidation_lines_delivery_route_liquidation",
                table: "delivery_route_liquidation_lines",
                column: "delivery_route_liquidation_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_liquidation_lines_tenant_id_delivery_route_l",
                table: "delivery_route_liquidation_lines",
                columns: new[] { "tenant_id", "delivery_route_liquidation_id", "delivery_route_stop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_liquidations_tenant_id_delivery_route_id",
                table: "delivery_route_liquidations",
                columns: new[] { "tenant_id", "delivery_route_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_liquidations_tenant_id_operation_key",
                table: "delivery_route_liquidations",
                columns: new[] { "tenant_id", "operation_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_stops_branch_id",
                table: "delivery_route_stops",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_stops_delivery_route_id",
                table: "delivery_route_stops",
                column: "delivery_route_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_stops_tenant_id_completion_operation_key",
                table: "delivery_route_stops",
                columns: new[] { "tenant_id", "completion_operation_key" },
                unique: true,
                filter: "completion_operation_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_stops_tenant_id_delivery_route_id_sequence",
                table: "delivery_route_stops",
                columns: new[] { "tenant_id", "delivery_route_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_stops_tenant_id_failure_operation_key",
                table: "delivery_route_stops",
                columns: new[] { "tenant_id", "failure_operation_key" },
                unique: true,
                filter: "failure_operation_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_stops_tenant_id_order_id",
                table: "delivery_route_stops",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_routes_branch_id",
                table: "delivery_routes",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_routes_tenant_id_branch_id_status",
                table: "delivery_routes",
                columns: new[] { "tenant_id", "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_routes_tenant_id_created_at_utc_id",
                table: "delivery_routes",
                columns: new[] { "tenant_id", "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_routes_tenant_id_creation_operation_key",
                table: "delivery_routes",
                columns: new[] { "tenant_id", "creation_operation_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_proofs");

            migrationBuilder.DropTable(
                name: "delivery_route_candidates");

            migrationBuilder.DropTable(
                name: "delivery_route_liquidation_lines");

            migrationBuilder.DropTable(
                name: "delivery_route_stops");

            migrationBuilder.DropTable(
                name: "delivery_route_liquidations");

            migrationBuilder.DropTable(
                name: "delivery_routes");
        }
    }
}
