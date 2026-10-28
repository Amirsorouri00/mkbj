using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "jobs");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateTable(
                name: "attribute_blueprints",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    data_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    previous_status = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_blueprints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "category_attribute_assignments",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    category_id = table.Column<long>(type: "bigint", nullable: false),
                    attribute_blueprint_id = table.Column<long>(type: "bigint", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_filterable = table.Column<bool>(type: "boolean", nullable: false),
                    is_searchable = table.Column<bool>(type: "boolean", nullable: false),
                    is_in_title = table.Column<bool>(type: "boolean", nullable: false),
                    title_order = table.Column<int>(type: "integer", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_variant_generating = table.Column<bool>(type: "boolean", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category_attribute_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category_attribute_exclusions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    category_id = table.Column<long>(type: "bigint", nullable: false),
                    source_assignment_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category_attribute_exclusions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job",
                schema: "jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job", x => x.id);
                    table.CheckConstraint("ck_job_status", "status IN ('Queued','Running','Completed','Failed')");
                });

            migrationBuilder.CreateTable(
                name: "measurement_units",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    sku = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    price_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    price_currency = table.Column<string>(type: "char(3)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recurring",
                schema: "jobs",
                columns: table => new
                {
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring", x => x.type);
                });

            migrationBuilder.CreateTable(
                name: "category_products",
                schema: "catalog",
                columns: table => new
                {
                    category_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category_products", x => new { x.category_id, x.product_id });
                    table.ForeignKey(
                        name: "fk_category_products_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attribute_blueprint_option",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    attribute_blueprint_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    measurement_unit_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_blueprint_option", x => x.id);
                    table.ForeignKey(
                        name: "fk_attribute_blueprint_option_attribute_blueprints_attribute_b",
                        column: x => x.attribute_blueprint_id,
                        principalSchema: "catalog",
                        principalTable: "attribute_blueprints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attribute_blueprint_option_measurement_units_measurement_un",
                        column: x => x.measurement_unit_id,
                        principalSchema: "catalog",
                        principalTable: "measurement_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attribute_blueprint_option_attribute_blueprint_id",
                schema: "catalog",
                table: "attribute_blueprint_option",
                column: "attribute_blueprint_id");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_blueprint_option_measurement_unit_id",
                schema: "catalog",
                table: "attribute_blueprint_option",
                column: "measurement_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_blueprints_name",
                schema: "catalog",
                table: "attribute_blueprints",
                column: "name",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id_normalized_name",
                schema: "catalog",
                table: "categories",
                columns: new[] { "parent_id", "normalized_name" },
                unique: true,
                filter: "status <> 2");

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id_status_display_order_id",
                schema: "catalog",
                table: "categories",
                columns: new[] { "parent_id", "status", "display_order", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_slug",
                schema: "catalog",
                table: "categories",
                column: "slug",
                unique: true,
                filter: "status <> 2");

            migrationBuilder.CreateIndex(
                name: "ix_category_attribute_assignments_category_id",
                schema: "catalog",
                table: "category_attribute_assignments",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_attribute_assignments_category_id_attribute_bluepr",
                schema: "catalog",
                table: "category_attribute_assignments",
                columns: new[] { "category_id", "attribute_blueprint_id" },
                unique: true,
                filter: "removed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_category_attribute_assignments_category_id_is_primary",
                schema: "catalog",
                table: "category_attribute_assignments",
                columns: new[] { "category_id", "is_primary" },
                filter: "removed_at IS NULL AND is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_category_attribute_exclusions_category_id",
                schema: "catalog",
                table: "category_attribute_exclusions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_attribute_exclusions_category_id_source_assignment",
                schema: "catalog",
                table: "category_attribute_exclusions",
                columns: new[] { "category_id", "source_assignment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_products_product_id_category_id",
                schema: "catalog",
                table: "category_products",
                columns: new[] { "product_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_job_status_run_at",
                schema: "jobs",
                table: "job",
                columns: new[] { "status", "run_at" });

            migrationBuilder.CreateIndex(
                name: "ux_job_dedupe_key_queued",
                schema: "jobs",
                table: "job",
                column: "dedupe_key",
                unique: true,
                filter: "status = 'Queued'");

            migrationBuilder.CreateIndex(
                name: "ix_measurement_units_name",
                schema: "catalog",
                table: "measurement_units",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_measurement_units_symbol",
                schema: "catalog",
                table: "measurement_units",
                column: "symbol",
                unique: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attribute_blueprint_option",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "category_attribute_assignments",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "category_attribute_exclusions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "category_products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "job",
                schema: "jobs");

            migrationBuilder.DropTable(
                name: "products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "recurring",
                schema: "jobs");

            migrationBuilder.DropTable(
                name: "attribute_blueprints",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "measurement_units",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "catalog");
        }
    }
}
