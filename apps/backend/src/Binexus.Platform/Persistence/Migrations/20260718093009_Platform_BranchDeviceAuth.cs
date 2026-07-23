using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Platform_BranchDeviceAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "branch_devices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE branch_devices
                SET security_stamp = replace(gen_random_uuid()::text, '-', '')
                WHERE security_stamp = '' OR security_stamp IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "device_auth_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nonce = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_auth_challenges", x => x.id);
                    table.CheckConstraint("ck_device_auth_challenges_status", "status IN ('Open', 'Consumed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_auth_challenges_branch_instance_id_device_id_status",
                table: "device_auth_challenges",
                columns: new[] { "branch_instance_id", "device_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_device_auth_challenges_expires_at_utc",
                table: "device_auth_challenges",
                column: "expires_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_auth_challenges");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "branch_devices");
        }
    }
}
