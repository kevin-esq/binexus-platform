using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Logistics_CloseAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "included",
                table: "delivery_route_liquidation_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                table: "delivery_route_liquidation_lines",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "delivery_proof_upload_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_proof_upload_intents", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_proof_upload_intents_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_features",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_features", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_features_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_route_stops_tenant_id_delivery_route_id_order_id",
                table: "delivery_route_stops",
                columns: new[] { "tenant_id", "delivery_route_id", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_proofs_photo_object_key",
                table: "delivery_proofs",
                column: "photo_object_key",
                unique: true,
                filter: "photo_object_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_proofs_signature_object_key",
                table: "delivery_proofs",
                column: "signature_object_key",
                unique: true,
                filter: "signature_object_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_proof_upload_intents_tenant_id_operation_key",
                table: "delivery_proof_upload_intents",
                columns: new[] { "tenant_id", "operation_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_features_tenant_id_key",
                table: "tenant_features",
                columns: new[] { "tenant_id", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_proof_upload_intents");

            migrationBuilder.DropTable(
                name: "tenant_features");

            migrationBuilder.DropIndex(
                name: "ix_delivery_route_stops_tenant_id_delivery_route_id_order_id",
                table: "delivery_route_stops");

            migrationBuilder.DropIndex(
                name: "ix_delivery_proofs_photo_object_key",
                table: "delivery_proofs");

            migrationBuilder.DropIndex(
                name: "ix_delivery_proofs_signature_object_key",
                table: "delivery_proofs");

            migrationBuilder.DropColumn(
                name: "included",
                table: "delivery_route_liquidation_lines");

            migrationBuilder.DropColumn(
                name: "payment_method",
                table: "delivery_route_liquidation_lines");
        }
    }
}
