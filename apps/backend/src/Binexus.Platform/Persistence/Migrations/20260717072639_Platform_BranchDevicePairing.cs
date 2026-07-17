using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Platform_BranchDevicePairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branch_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    public_key_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    credential_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    pairing_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_devices", x => x.id);
                    table.CheckConstraint("ck_branch_devices_status", "status IN ('PendingConfirmation', 'Active', 'Revoked')");
                });

            migrationBuilder.CreateTable(
                name: "branch_terminals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_terminals", x => x.id);
                    table.CheckConstraint("ck_branch_terminals_status", "status IN ('PendingConfirmation', 'Active', 'Disabled')");
                });

            migrationBuilder.CreateTable(
                name: "device_pairing_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    branch_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pairing_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pairing_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terminal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    public_key_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    credential_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pairing_receipt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    nonce = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_pairing_challenges", x => x.id);
                    table.CheckConstraint("ck_device_pairing_challenges_phase", "phase IN ('Exchange', 'Confirmation', 'ReceiptReissue')");
                    table.CheckConstraint("ck_device_pairing_challenges_phase_targets", "(phase = 'Exchange' AND pairing_session_id IS NOT NULL) OR (phase = 'Confirmation' AND pairing_request_id IS NOT NULL AND pairing_receipt_hash IS NOT NULL) OR (phase = 'ReceiptReissue' AND pairing_request_id IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "device_pairing_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pairing_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    public_key_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    credential_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requested_terminal_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    requested_terminal_name_normalized = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status_token_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pairing_receipt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_pairing_requests", x => x.id);
                    table.CheckConstraint("ck_device_pairing_requests_status", "status IN ('PendingApproval', 'Approved', 'Rejected', 'Expired', 'Completed')");
                });

            migrationBuilder.CreateTable(
                name: "device_pairing_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    failed_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_pairing_sessions", x => x.id);
                    table.CheckConstraint("ck_device_pairing_sessions_status", "status IN ('Open', 'Consumed', 'Expired')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_branch_devices_branch_instance_id_credential_hash",
                table: "branch_devices",
                columns: new[] { "branch_instance_id", "credential_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_devices_branch_instance_id_public_key_fingerprint",
                table: "branch_devices",
                columns: new[] { "branch_instance_id", "public_key_fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_devices_pairing_request_id",
                table: "branch_devices",
                column: "pairing_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_terminals_branch_instance_id_normalized_name",
                table: "branch_terminals",
                columns: new[] { "branch_instance_id", "normalized_name" },
                unique: true,
                filter: "status IN ('PendingConfirmation', 'Active')");

            migrationBuilder.CreateIndex(
                name: "ix_branch_terminals_device_id",
                table: "branch_terminals",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_pairing_challenges_branch_instance_id_expires_at_utc",
                table: "device_pairing_challenges",
                columns: new[] { "branch_instance_id", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_device_pairing_challenges_pairing_request_id",
                table: "device_pairing_challenges",
                column: "pairing_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_pairing_requests_pairing_session_id_device_id",
                table: "device_pairing_requests",
                columns: new[] { "pairing_session_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_pairing_sessions_branch_instance_id_status",
                table: "device_pairing_sessions",
                columns: new[] { "branch_instance_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_device_pairing_sessions_code_hash",
                table: "device_pairing_sessions",
                column: "code_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_devices");

            migrationBuilder.DropTable(
                name: "branch_terminals");

            migrationBuilder.DropTable(
                name: "device_pairing_challenges");

            migrationBuilder.DropTable(
                name: "device_pairing_requests");

            migrationBuilder.DropTable(
                name: "device_pairing_sessions");
        }
    }
}
