using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <inheritdoc />
    public partial class CatalogAttributeCanonicalKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Refuses rather than backfills. The canonical key is produced by a C# normalizer
            // (NFKD, then dropping combining marks) that Postgres cannot reproduce, so a guessed
            // backfill would diverge from what the application computes the moment anyone renames
            // an attribute. Worse, every backfilled row would share the key '' in the global
            // scope, and the NULLS NOT DISTINCT index below would then reject all but one of
            // them. Nothing has shipped yet; if that changes, write the backfill from the
            // application instead.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM catalog.attribute_blueprints) THEN
                        RAISE EXCEPTION
                            'CatalogAttributeCanonicalKeys cannot run on a non-empty attribute_blueprints table: canonical_key must be computed by the application.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<string[]>(
                name: "aliases",
                schema: "catalog",
                table: "attribute_blueprints",
                type: "text[]",
                nullable: false,
                defaultValue: Array.Empty<string>());

            migrationBuilder.AddColumn<string>(
                name: "canonical_key",
                schema: "catalog",
                table: "attribute_blueprints",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "scope",
                schema: "catalog",
                table: "attribute_blueprints",
                type: "integer",
                nullable: false,
                // AttributeScope.Global. EF scaffolds 0, which is not a defined value of the enum.
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "scope_category_id",
                schema: "catalog",
                table: "attribute_blueprints",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "unit_id",
                schema: "catalog",
                table: "attribute_blueprints",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_attribute_blueprints_aliases",
                schema: "catalog",
                table: "attribute_blueprints",
                column: "aliases")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_blueprints_scope_category_id",
                schema: "catalog",
                table: "attribute_blueprints",
                column: "scope_category_id",
                filter: "scope_category_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_blueprints_unit_id",
                schema: "catalog",
                table: "attribute_blueprints",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ux_attribute_blueprints_scope_key",
                schema: "catalog",
                table: "attribute_blueprints",
                columns: new[] { "scope_category_id", "canonical_key" },
                unique: true,
                filter: "deleted_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddForeignKey(
                name: "fk_attribute_blueprints_measurement_units_unit_id",
                schema: "catalog",
                table: "attribute_blueprints",
                column: "unit_id",
                principalSchema: "catalog",
                principalTable: "measurement_units",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_attribute_blueprints_measurement_units_unit_id",
                schema: "catalog",
                table: "attribute_blueprints");

            migrationBuilder.DropIndex(
                name: "ix_attribute_blueprints_aliases",
                schema: "catalog",
                table: "attribute_blueprints");

            migrationBuilder.DropIndex(
                name: "ix_attribute_blueprints_scope_category_id",
                schema: "catalog",
                table: "attribute_blueprints");

            migrationBuilder.DropIndex(
                name: "ix_attribute_blueprints_unit_id",
                schema: "catalog",
                table: "attribute_blueprints");

            migrationBuilder.DropIndex(
                name: "ux_attribute_blueprints_scope_key",
                schema: "catalog",
                table: "attribute_blueprints");

            migrationBuilder.DropColumn(
                name: "aliases",
                schema: "catalog",
                table: "attribute_blueprints");

            migrationBuilder.DropColumn(
                name: "canonical_key",
                schema: "catalog",
                table: "attribute_blueprints");

            migrationBuilder.DropColumn(
                name: "scope",
                schema: "catalog",
                table: "attribute_blueprints");

            migrationBuilder.DropColumn(
                name: "scope_category_id",
                schema: "catalog",
                table: "attribute_blueprints");

            migrationBuilder.DropColumn(
                name: "unit_id",
                schema: "catalog",
                table: "attribute_blueprints");
        }
    }
}
