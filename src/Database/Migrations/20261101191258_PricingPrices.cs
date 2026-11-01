using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class PricingPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "variant_prices",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    base_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", nullable: false),
                    provenance_observation_id = table.Column<long>(type: "bigint", nullable: true),
                    provenance_source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    provenance_collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provenance_original_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    provenance_original_currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    provenance_tax_included = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variant_prices", x => x.id);
                    table.CheckConstraint("ck_variant_prices_positive", "base_amount_minor > 0");
                });

            migrationBuilder.CreateTable(
                name: "currency_overrides",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    variant_price_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    price_list = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currency_overrides", x => x.id);
                    table.CheckConstraint("ck_currency_overrides_positive", "amount_minor > 0");
                    table.ForeignKey(
                        name: "fk_currency_overrides_variant_prices_variant_price_id",
                        column: x => x.variant_price_id,
                        principalSchema: "pricing",
                        principalTable: "variant_prices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_currency_overrides_price_currency_list",
                schema: "pricing",
                table: "currency_overrides",
                columns: new[] { "variant_price_id", "currency", "price_list" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_variant_prices_variant",
                schema: "pricing",
                table: "variant_prices",
                column: "variant_id",
                unique: true);
            // Not an EF entity: nothing in C# writes a row at a time, and the storefront's SQL
            // joins it directly. Modelling it would buy change tracking nobody uses and a
            // migration diff every time the projection's shape moved.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS pricing.resolved_fx_rates (
                    base            char(3)        NOT NULL,
                    quote           char(3)        NOT NULL,
                    rate            numeric(18,10) NOT NULL CHECK (rate > 0),
                    provenance      text           NOT NULL,
                    fx_rate_id      bigint         NULL,
                    fx_override_id  bigint         NULL,
                    rate_at         timestamptz    NOT NULL,
                    valid_until     timestamptz    NULL,
                    PRIMARY KEY (base, quote)
                )
                """);

            migrationBuilder.Sql(
                "CREATE SEQUENCE IF NOT EXISTS pricing.price_version AS bigint START WITH 1");

            // Postgres' own round() is half-away-from-zero, which drifts upward over a catalogue
            // of conversions. The storefront views round the same way the C# resolver does, or
            // the price on a listing page and the price in a quote would differ by a cent.
            // IMMUTABLE so it may be used in an index or a materialized view.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION pricing.round_half_even(x numeric)
                RETURNS numeric
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                AS $$
                    SELECT CASE
                        WHEN x - floor(x) = 0.5
                            THEN CASE WHEN mod(floor(x), 2) = 0 THEN floor(x) ELSE floor(x) + 1 END
                        ELSE round(x)
                    END
                $$
                """);
        }



        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS pricing.round_half_even(numeric)");
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS pricing.price_version");
            migrationBuilder.Sql("DROP TABLE IF EXISTS pricing.resolved_fx_rates");

            migrationBuilder.DropTable(
                name: "currency_overrides",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "variant_prices",
                schema: "pricing");
        }
    }
}
