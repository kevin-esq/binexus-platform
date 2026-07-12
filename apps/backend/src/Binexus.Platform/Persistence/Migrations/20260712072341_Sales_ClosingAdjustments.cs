using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sales_ClosingAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_captures_sale_sale_id",
                table: "payment_captures");

            migrationBuilder.DropForeignKey(
                name: "fk_payment_captures_sales_session_session_id",
                table: "payment_captures");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_sales_session_session_id",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_sales_session_id",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_payment_captures_sale_id",
                table: "payment_captures");

            migrationBuilder.DropIndex(
                name: "ix_payment_captures_session_id",
                table: "payment_captures");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_sales_sessions_tenant_id",
                table: "sales_sessions",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_sales_tenant_id_session",
                table: "sales",
                columns: new[] { "tenant_id", "id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_captures_tenant_id_sale_id_session_id",
                table: "payment_captures",
                columns: new[] { "tenant_id", "sale_id", "session_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_payment_captures_sales_tenant_id_sale_id_session_id",
                table: "payment_captures",
                columns: new[] { "tenant_id", "sale_id", "session_id" },
                principalTable: "sales",
                principalColumns: new[] { "tenant_id", "id", "session_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_sales_session_tenant_id_session_id",
                table: "sales",
                columns: new[] { "tenant_id", "session_id" },
                principalTable: "sales_sessions",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_captures_sales_tenant_id_sale_id_session_id",
                table: "payment_captures");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_sales_session_tenant_id_session_id",
                table: "sales");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_sales_sessions_tenant_id",
                table: "sales_sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_sales_tenant_id_session",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_payment_captures_tenant_id_sale_id_session_id",
                table: "payment_captures");

            migrationBuilder.CreateIndex(
                name: "ix_sales_session_id",
                table: "sales",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_captures_sale_id",
                table: "payment_captures",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_captures_session_id",
                table: "payment_captures",
                column: "session_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_captures_sale_sale_id",
                table: "payment_captures",
                column: "sale_id",
                principalTable: "sales",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_payment_captures_sales_session_session_id",
                table: "payment_captures",
                column: "session_id",
                principalTable: "sales_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_sales_session_session_id",
                table: "sales",
                column: "session_id",
                principalTable: "sales_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
