using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations;

/// <summary>
/// Gives the attribute-blueprint options table the plural name every other catalog table uses.
/// <para>
/// Hand-written as a rename. EF scaffolded a drop-and-create — correct for the schema, and it
/// would have discarded every option row on the way. A rename moves the table and its indexes
/// without touching the data, which is what "rename a table" should cost.
/// </para>
/// </summary>
public partial class CatalogSnakeTableNames : Migration
{
    private const string Schema = "catalog";

    private const string OldTable = "attribute_blueprint_option";
    private const string NewTable = "attribute_blueprint_options";

    /// <summary>
    /// Postgres carries indexes and constraints through a table rename but keeps their old
    /// names, so each is renamed explicitly to the name the EF model expects. Otherwise the next
    /// migration would see them as missing and scaffold them again.
    /// </summary>
    private static readonly (string Old, string New)[] Renames =
    [
        ("pk_attribute_blueprint_option", "pk_attribute_blueprint_options"),
        ("ix_attribute_blueprint_option_attribute_blueprint_id", "ix_attribute_blueprint_options_attribute_blueprint_id"),
        ("ix_attribute_blueprint_option_measurement_unit_id", "ix_attribute_blueprint_options_measurement_unit_id"),
        ("fk_attribute_blueprint_option_attribute_blueprints_attribute_b", "fk_attribute_blueprint_options_attribute_blueprints_attribute_"),
        ("fk_attribute_blueprint_option_measurement_units_measurement_un", "fk_attribute_blueprint_options_measurement_units_measurement_u"),
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.RenameTable(
            name: OldTable, schema: Schema, newName: NewTable, newSchema: Schema);

        foreach (var (oldName, newName) in Renames)
            Rename(migrationBuilder, oldName, newName);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        foreach (var (oldName, newName) in Renames)
            Rename(migrationBuilder, newName, oldName);

        migrationBuilder.RenameTable(
            name: NewTable, schema: Schema, newName: OldTable, newSchema: Schema);
    }

    /// <summary>
    /// One statement for indexes and constraints alike. A primary key and a unique index are both
    /// backed by an index in Postgres, so ALTER INDEX renames them and their constraint together;
    /// a foreign key is not, hence the fallback.
    /// </summary>
    private static void Rename(MigrationBuilder migrationBuilder, string oldName, string newName) =>
        migrationBuilder.Sql($"""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_indexes
                           WHERE schemaname = '{Schema}' AND indexname = '{oldName}') THEN
                    EXECUTE 'ALTER INDEX {Schema}.{oldName} RENAME TO {newName}';
                ELSIF EXISTS (SELECT 1 FROM pg_constraint
                              WHERE conname = '{oldName}'
                                AND connamespace = '{Schema}'::regnamespace) THEN
                    EXECUTE 'ALTER TABLE {Schema}.{NewTable} RENAME CONSTRAINT {oldName} TO {newName}';
                END IF;
            END $$;
            """);
}
