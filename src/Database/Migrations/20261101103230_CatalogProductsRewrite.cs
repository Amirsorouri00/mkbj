using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class CatalogProductsRewrite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Refuses rather than migrates. The old products table held a price and a single SKU;
            // the new one holds neither, and its variants, images and identifiers live in tables
            // that did not exist. There is no row-by-row translation — a product's variants have
            // to be derived from source listings the catalogue does not have yet.
            //
            // On an empty table the column renames below are safe and are only EF's way of
            // reusing compatible column types (bigint to bigint, boolean to boolean). With rows
            // present they would carry a price into a category id, so this guard is what makes
            // them acceptable at all.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM catalog.products) THEN
                        RAISE EXCEPTION
                            'CatalogProductsRewrite cannot run on a non-empty products table: the old and new product shapes have no row-by-row translation.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_products_created_at",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_sku",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "price_currency",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "sku",
                schema: "catalog",
                table: "products");

            migrationBuilder.RenameColumn(
                name: "price_amount_minor",
                schema: "catalog",
                table: "products",
                newName: "primary_category_id");

            migrationBuilder.RenameColumn(
                name: "name",
                schema: "catalog",
                table: "products",
                newName: "title");

            migrationBuilder.RenameColumn(
                name: "is_active",
                schema: "catalog",
                table: "products",
                newName: "deactivated_by_admin");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "catalog",
                table: "products",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "brand_id",
                schema: "catalog",
                table: "products",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "merged_into_product_id",
                schema: "catalog",
                table: "products",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mpn",
                schema: "catalog",
                table: "products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "short_description",
                schema: "catalog",
                table: "products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "slug",
                schema: "catalog",
                table: "products",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long[]>(
                name: "source_refs",
                schema: "catalog",
                table: "products",
                type: "bigint[]",
                nullable: false,
                defaultValue: Array.Empty<long>());

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "catalog",
                table: "products",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "product_identifiers",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_identifiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_identifiers_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_media",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    alt = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    usage_rights = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_media", x => x.id);
                    table.CheckConstraint("ck_product_media_usage_rights", "usage_rights IN ('Allowed', 'RightsUnknown', 'Denied')");
                    table.ForeignKey(
                        name: "fk_product_media_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variants",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    sku = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attribute_signature = table.Column<string>(type: "text", nullable: false),
                    attribute_signature_hash = table.Column<string>(type: "char(64)", nullable: false),
                    attribute_values = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    deactivated_by_admin = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variants", x => x.id);
                    table.ForeignKey(
                        name: "fk_variants_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_primary_category_id",
                schema: "catalog",
                table: "products",
                column: "primary_category_id");

            migrationBuilder.CreateIndex(
                name: "ux_products_brand_mpn",
                schema: "catalog",
                table: "products",
                columns: new[] { "brand_id", "mpn" },
                unique: true,
                filter: "mpn IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_products_slug",
                schema: "catalog",
                table: "products",
                column: "slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_status",
                schema: "catalog",
                table: "products",
                sql: "status IN ('Draft', 'Active', 'Deactivated')");

            migrationBuilder.CreateIndex(
                name: "ix_product_identifiers_product_id",
                schema: "catalog",
                table: "product_identifiers",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_identifiers_source",
                schema: "catalog",
                table: "product_identifiers",
                columns: new[] { "source_domain", "value" },
                unique: true,
                filter: "type = 'SourceId'");

            migrationBuilder.CreateIndex(
                name: "ux_product_identifiers_type_value",
                schema: "catalog",
                table: "product_identifiers",
                columns: new[] { "type", "value" },
                unique: true,
                filter: "type IN ('Gtin', 'Upc', 'Ean')");

            migrationBuilder.CreateIndex(
                name: "ix_product_media_product_id_sort_order",
                schema: "catalog",
                table: "product_media",
                columns: new[] { "product_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_variants_product_signature",
                schema: "catalog",
                table: "variants",
                columns: new[] { "product_id", "attribute_signature_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_variants_sku",
                schema: "catalog",
                table: "variants",
                column: "sku",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_products_brands_brand_id",
                schema: "catalog",
                table: "products",
                column: "brand_id",
                principalSchema: "catalog",
                principalTable: "brands",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_products_categories_primary_category_id",
                schema: "catalog",
                table: "products",
                column: "primary_category_id",
                principalSchema: "catalog",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_products_brands_brand_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "fk_products_categories_primary_category_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropTable(
                name: "product_identifiers",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_media",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "variants",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "ix_products_primary_category_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ux_products_brand_mpn",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ux_products_slug",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_status",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "brand_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "merged_into_product_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "mpn",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "short_description",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "slug",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "source_refs",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "catalog",
                table: "products");

            migrationBuilder.RenameColumn(
                name: "title",
                schema: "catalog",
                table: "products",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "primary_category_id",
                schema: "catalog",
                table: "products",
                newName: "price_amount_minor");

            migrationBuilder.RenameColumn(
                name: "deactivated_by_admin",
                schema: "catalog",
                table: "products",
                newName: "is_active");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "catalog",
                table: "products",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20000)",
                oldMaxLength: 20000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "price_currency",
                schema: "catalog",
                table: "products",
                type: "char(3)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sku",
                schema: "catalog",
                table: "products",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_products_created_at",
                schema: "catalog",
                table: "products",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_products_sku",
                schema: "catalog",
                table: "products",
                column: "sku",
                unique: true);
        }
    }
}
