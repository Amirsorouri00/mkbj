using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class CatalogBrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "brands",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    canonical_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provenance = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brands", x => x.id);
                    table.CheckConstraint("ck_brands_provenance", "provenance IN ('admin', 'import', 'seed')");
                });

            migrationBuilder.CreateTable(
                name: "brand_aliases",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    brand_id = table.Column<long>(type: "bigint", nullable: false),
                    alias = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brand_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_brand_aliases_brands_brand_id",
                        column: x => x.brand_id,
                        principalSchema: "catalog",
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_brand_aliases_brand_id",
                schema: "catalog",
                table: "brand_aliases",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ux_brand_aliases_alias",
                schema: "catalog",
                table: "brand_aliases",
                column: "alias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_brands_canonical_name",
                schema: "catalog",
                table: "brands",
                column: "canonical_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "brand_aliases",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "brands",
                schema: "catalog");
        }
    }
}
