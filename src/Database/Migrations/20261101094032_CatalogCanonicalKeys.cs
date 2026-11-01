using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class CatalogCanonicalKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_categories_parent_id_normalized_name",
                schema: "catalog",
                table: "categories");

            // canonical_key is NOT NULL with an empty default, which is only safe because no
            // deployment has categories yet. On a populated database every row would get '' and
            // the unique index below would then reject all but one of them — a failure that
            // reads as "duplicate category" long after the cause. Postgres cannot reproduce the
            // C# normalizer (NFKD plus mark stripping), so this refuses rather than guessing:
            // backfill through the application, then re-run.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM catalog.categories) THEN
                        RAISE EXCEPTION
                            'catalog.categories is not empty: canonical_key must be backfilled by the application before this migration can run';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<string[]>(
                name: "aliases",
                schema: "catalog",
                table: "categories",
                type: "text[]",
                nullable: false,
                defaultValue: Array.Empty<string>());

            migrationBuilder.AddColumn<string>(
                name: "canonical_key",
                schema: "catalog",
                table: "categories",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_categories_aliases",
                schema: "catalog",
                table: "categories",
                column: "aliases")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ux_categories_parent_canonical_key",
                schema: "catalog",
                table: "categories",
                columns: new[] { "parent_id", "canonical_key" },
                unique: true,
                filter: "status <> 2")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_categories_aliases",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "ux_categories_parent_canonical_key",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "aliases",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "canonical_key",
                schema: "catalog",
                table: "categories");

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id_normalized_name",
                schema: "catalog",
                table: "categories",
                columns: new[] { "parent_id", "normalized_name" },
                unique: true,
                filter: "status <> 2");
        }
    }
}
