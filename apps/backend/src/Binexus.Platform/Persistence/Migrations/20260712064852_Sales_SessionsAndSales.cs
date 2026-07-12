using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Binexus.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sales_SessionsAndSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terminal_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    opening_float_cents = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expected_closing_cents = table.Column<int>(type: "integer", nullable: true),
                    declared_closing_cents = table.Column<int>(type: "integer", nullable: true),
                    discrepancy_cents = table.Column<int>(type: "integer", nullable: true),
                    discrepancy_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    close_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    open_operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    close_operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_sessions", x => x.id);
                    table.CheckConstraint("ck_sales_sessions_currency_iso3", "char_length(currency) = 3");
                    table.CheckConstraint("ck_sales_sessions_opening_float_non_negative", "opening_float_cents >= 0");
                    table.CheckConstraint("ck_sales_sessions_status", "status IN ('OPEN','CLOSED')");
                    table.ForeignKey(
                        name: "fk_sales_sessions_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_sessions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terminal_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    total_cents = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cashier_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    operation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales", x => x.id);
                    table.CheckConstraint("ck_sales_currency_iso3", "char_length(currency) = 3");
                    table.CheckConstraint("ck_sales_status", "status IN ('COMPLETED')");
                    table.CheckConstraint("ck_sales_total_non_negative", "total_cents >= 0");
                    table.ForeignKey(
                        name: "fk_sales_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_sales_session_session_id",
                        column: x => x.session_id,
                        principalTable: "sales_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_captures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount_cents = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_captures", x => x.id);
                    table.CheckConstraint("ck_payment_captures_amount_positive", "amount_cents > 0");
                    table.CheckConstraint("ck_payment_captures_currency_iso3", "char_length(currency) = 3");
                    table.CheckConstraint("ck_payment_captures_method", "method IN ('CASH','CARD','TRANSFER')");
                    table.ForeignKey(
                        name: "fk_payment_captures_sale_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_payment_captures_sales_session_session_id",
                        column: x => x.session_id,
                        principalTable: "sales_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_captures_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    product_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_cents = table.Column<int>(type: "integer", nullable: false),
                    line_total_cents = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_lines", x => x.id);
                    table.CheckConstraint("ck_sale_lines_line_total_non_negative", "line_total_cents >= 0");
                    table.CheckConstraint("ck_sale_lines_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_sale_lines_unit_price_non_negative", "unit_price_cents >= 0");
                    table.ForeignKey(
                        name: "fk_sale_lines_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sale_lines_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_captures_sale_id",
                table: "payment_captures",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_captures_session_id",
                table: "payment_captures",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_captures_tenant_id_sale_id",
                table: "payment_captures",
                columns: new[] { "tenant_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_captures_tenant_id_session_id_method",
                table: "payment_captures",
                columns: new[] { "tenant_id", "session_id", "method" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_sale_id",
                table: "sale_lines",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_tenant_id_sale_id",
                table: "sale_lines",
                columns: new[] { "tenant_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_branch_id",
                table: "sales",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_session_id",
                table: "sales",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_id_operation_key",
                table: "sales",
                columns: new[] { "tenant_id", "operation_key" },
                unique: true,
                filter: "operation_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_id_session_id",
                table: "sales",
                columns: new[] { "tenant_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_sessions_branch_id",
                table: "sales_sessions",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_sessions_open_terminal_unique",
                table: "sales_sessions",
                columns: new[] { "tenant_id", "branch_id", "terminal_id" },
                unique: true,
                filter: "status = 'OPEN'");

            migrationBuilder.CreateIndex(
                name: "ix_sales_sessions_tenant_id_branch_id_terminal_id_status",
                table: "sales_sessions",
                columns: new[] { "tenant_id", "branch_id", "terminal_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_sessions_tenant_id_close_operation_key",
                table: "sales_sessions",
                columns: new[] { "tenant_id", "close_operation_key" },
                unique: true,
                filter: "close_operation_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_sessions_tenant_id_open_operation_key",
                table: "sales_sessions",
                columns: new[] { "tenant_id", "open_operation_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_captures");

            migrationBuilder.DropTable(
                name: "sale_lines");

            migrationBuilder.DropTable(
                name: "sales");

            migrationBuilder.DropTable(
                name: "sales_sessions");
        }
    }
}
