using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesCarts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carts",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cart_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    displayed_unit_price_minor = table.Column<long>(type: "bigint", nullable: false),
                    price_quoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cart_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_lines", x => x.id);
                    table.CheckConstraint("ck_cart_lines_quantity", "quantity BETWEEN 1 AND 999");
                    table.ForeignKey(
                        name: "fk_cart_lines_carts_cart_id",
                        column: x => x.cart_id,
                        principalSchema: "sales",
                        principalTable: "carts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_cart_lines_cart_variant",
                schema: "sales",
                table: "cart_lines",
                columns: new[] { "cart_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_carts_customer",
                schema: "sales",
                table: "carts",
                column: "customer_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "carts",
                schema: "sales");
        }
    }
}
