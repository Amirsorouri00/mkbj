using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class PricingInit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.EnsureSchema(
                name: "pricing");

            migrationBuilder.CreateTable(
                name: "currencies",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "char(3)", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    minor_units = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fx_rate_overrides",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    @base = table.Column<string>(name: "base", type: "char(3)", nullable: false),
                    quote = table.Column<string>(type: "char(3)", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    set_by = table.Column<Guid>(type: "uuid", nullable: false),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fx_rate_overrides", x => x.id);
                    table.CheckConstraint("ck_fx_rate_overrides_positive", "rate > 0");
                });

            migrationBuilder.CreateTable(
                name: "fx_rates",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    @base = table.Column<string>(name: "base", type: "char(3)", nullable: false),
                    quote = table.Column<string>(type: "char(3)", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fx_rates", x => x.id);
                    table.CheckConstraint("ck_fx_rates_positive", "rate > 0");
                });

            migrationBuilder.InsertData(
                schema: "pricing",
                table: "currencies",
                columns: new[] { "id", "code", "is_enabled", "minor_units", "updated_at" },
                values: new object[,]
                {
                    { 702L, "SGD", true, 2, null },
                    { 784L, "AED", true, 2, null },
                    { 826L, "GBP", true, 2, null },
                    { 840L, "USD", true, 2, null },
                    { 978L, "EUR", true, 2, null }
                });

            migrationBuilder.CreateIndex(
                name: "ux_currencies_code",
                schema: "pricing",
                table: "currencies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fx_rate_overrides_pair",
                schema: "pricing",
                table: "fx_rate_overrides",
                columns: new[] { "base", "quote" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fx_rates_pair_fetched_at",
                schema: "pricing",
                table: "fx_rates",
                columns: new[] { "base", "quote", "fetched_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_fx_rates_pair_provider_date",
                schema: "pricing",
                table: "fx_rates",
                columns: new[] { "base", "quote", "provider", "rate_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "currencies",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "fx_rate_overrides",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "fx_rates",
                schema: "pricing");
        }
    }
}
