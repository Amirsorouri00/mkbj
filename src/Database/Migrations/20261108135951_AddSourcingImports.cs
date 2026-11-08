using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSourcingImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sourcing");

            migrationBuilder.CreateTable(
                name: "import_jobs",
                schema: "sourcing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sources = table.Column<string[]>(type: "text[]", nullable: false),
                    queries = table.Column<string[]>(type: "text[]", nullable: false),
                    target_category_id = table.Column<long>(type: "bigint", nullable: true),
                    include_domains = table.Column<string[]>(type: "text[]", nullable: false),
                    exclude_domains = table.Column<string[]>(type: "text[]", nullable: false),
                    publish_policy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    file_rights_attested = table.Column<bool>(type: "boolean", nullable: false),
                    file_object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    file_format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    mapping = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    step_sequence = table.Column<long>(type: "bigint", nullable: false),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checkpoint_cursor = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    checkpoint_phase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    checkpoint_through_id = table.Column<long>(type: "bigint", nullable: false),
                    count_discovered = table.Column<int>(type: "integer", nullable: false),
                    count_duplicate = table.Column<int>(type: "integer", nullable: false),
                    count_extracted = table.Column<int>(type: "integer", nullable: false),
                    count_invalid = table.Column<int>(type: "integer", nullable: false),
                    count_published = table.Column<int>(type: "integer", nullable: false),
                    count_review = table.Column<int>(type: "integer", nullable: false),
                    count_valid = table.Column<int>(type: "integer", nullable: false),
                    max_depth = table.Column<int>(type: "integer", nullable: false),
                    max_pages = table.Column<int>(type: "integer", nullable: false),
                    max_records = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_jobs", x => x.id);
                    table.CheckConstraint("ck_import_jobs_limits", "max_pages BETWEEN 1 AND 5000 AND max_depth BETWEEN 0 AND 5 AND max_records BETWEEN 1 AND 10000");
                });

            migrationBuilder.CreateTable(
                name: "source_listings",
                schema: "sourcing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    image_urls = table.Column<string[]>(type: "text[]", nullable: false),
                    rights = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    staged_record_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_listings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_policies",
                schema: "sourcing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    crawl_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    content_use_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    image_use_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staged_records",
                schema: "sourcing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_job_id = table.Column<long>(type: "bigint", nullable: false),
                    last_job_id = table.Column<long>(type: "bigint", nullable: false),
                    source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    source_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    raw_json = table.Column<string>(type: "jsonb", nullable: false),
                    raw_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_json = table.Column<string>(type: "jsonb", nullable: true),
                    image_keys = table.Column<string[]>(type: "text[]", nullable: false),
                    match = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category_override_id = table.Column<long>(type: "bigint", nullable: true),
                    rights_approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_product_id = table.Column<long>(type: "bigint", nullable: true),
                    published_variant_ids = table.Column<long[]>(type: "bigint[]", nullable: false),
                    publish_skipped_deactivated = table.Column<bool>(type: "boolean", nullable: false),
                    source_listing_id = table.Column<long>(type: "bigint", nullable: true),
                    staging_cleaned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issues = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staged_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_observations",
                schema: "sourcing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    variant_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    tax_included = table.Column<bool>(type: "boolean", nullable: true),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_listing_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_observations", x => x.id);
                    table.CheckConstraint("ck_price_observations_amount", "amount_minor > 0");
                    table.ForeignKey(
                        name: "fk_price_observations_source_listings_source_listing_id",
                        column: x => x.source_listing_id,
                        principalSchema: "sourcing",
                        principalTable: "source_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_status",
                schema: "sourcing",
                table: "import_jobs",
                columns: new[] { "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_price_observations_grain",
                schema: "sourcing",
                table: "price_observations",
                columns: new[] { "source_listing_id", "variant_key", "collected_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_source_listings_domain_source",
                schema: "sourcing",
                table: "source_listings",
                columns: new[] { "domain", "source_id" },
                unique: true,
                filter: "source_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_source_listings_url",
                schema: "sourcing",
                table: "source_listings",
                column: "url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_source_policies_domain",
                schema: "sourcing",
                table: "source_policies",
                column: "domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staged_records_job_status",
                schema: "sourcing",
                table: "staged_records",
                columns: new[] { "last_job_id", "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_staged_records_fingerprint",
                schema: "sourcing",
                table: "staged_records",
                column: "fingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_jobs",
                schema: "sourcing");

            migrationBuilder.DropTable(
                name: "price_observations",
                schema: "sourcing");

            migrationBuilder.DropTable(
                name: "source_policies",
                schema: "sourcing");

            migrationBuilder.DropTable(
                name: "staged_records",
                schema: "sourcing");

            migrationBuilder.DropTable(
                name: "source_listings",
                schema: "sourcing");
        }
    }
}
