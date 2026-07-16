using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Platform_BranchActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_branch_instances_status_ready_for_activation",
                table: "branch_instances");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "activated_at_utc",
                table: "branch_instances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                table: "branch_instances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cloud_activation_id",
                table: "branch_instances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "branch_instances",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "branch_activation_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_key_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    installation_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nonce = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_activation_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "branch_activations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reserved_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    adopted_branch_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    public_key_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    installation_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    activation_receipt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    failed_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reserved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_activations", x => x.id);
                    table.CheckConstraint("ck_branch_activations_status", "status IN ('Open', 'Reserved', 'Consumed', 'Expired')");
                });

            migrationBuilder.CreateTable(
                name: "cloud_branch_instances",
                columns: table => new
                {
                    branch_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    installation_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    public_key = table.Column<string>(type: "text", nullable: false),
                    public_key_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    activation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activating_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cloud_branch_instances", x => x.branch_instance_id);
                    table.CheckConstraint("ck_cloud_branch_instances_status", "status IN ('Activating', 'Active')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_branch_instances_status",
                table: "branch_instances",
                sql: "status IN ('ReadyForActivation', 'Active')");

            migrationBuilder.CreateIndex(
                name: "ix_branch_activation_challenges_branch_instance_id_expires_at_",
                table: "branch_activation_challenges",
                columns: new[] { "branch_instance_id", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_activations_code_hash",
                table: "branch_activations",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_activations_tenant_id_branch_id",
                table: "branch_activations",
                columns: new[] { "tenant_id", "branch_id" },
                unique: true,
                filter: "status IN ('Open', 'Reserved')");

            migrationBuilder.CreateIndex(
                name: "ix_cloud_branch_instances_tenant_id_branch_id",
                table: "cloud_branch_instances",
                columns: new[] { "tenant_id", "branch_id" },
                unique: true,
                filter: "status IN ('Activating', 'Active')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_activation_challenges");

            migrationBuilder.DropTable(
                name: "branch_activations");

            migrationBuilder.DropTable(
                name: "cloud_branch_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_branch_instances_status",
                table: "branch_instances");

            migrationBuilder.DropColumn(
                name: "activated_at_utc",
                table: "branch_instances");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "branch_instances");

            migrationBuilder.DropColumn(
                name: "cloud_activation_id",
                table: "branch_instances");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "branch_instances");

            migrationBuilder.AddCheckConstraint(
                name: "ck_branch_instances_status_ready_for_activation",
                table: "branch_instances",
                sql: "status = 'ReadyForActivation'");
        }
    }
}
