using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Platform_BranchInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branch_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    singleton_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_instances", x => x.id);
                    table.CheckConstraint("ck_branch_instances_singleton_key_local", "singleton_key = 'local'");
                    table.CheckConstraint("ck_branch_instances_status_ready_for_activation", "status = 'ReadyForActivation'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_branch_instances_singleton_key",
                table: "branch_instances",
                column: "singleton_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_instances");
        }
    }
}
