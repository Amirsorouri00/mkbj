using Microsoft.EntityFrameworkCore.Migrations;

namespace EwaysVNext.Database.Migrations;

/// <summary>
/// SQL Server cannot add or drop the IDENTITY property of an existing column (EF's AlterColumn
/// throws "To change the IDENTITY property of a column, the column needs to be dropped and
/// recreated"). These helpers rebuild the key column instead, preserving every existing value.
/// </summary>
internal static class IdentityColumnMigrationExtensions
{
    private const string TempColumn = "Id_NoIdentity";

    /// <summary>
    /// Replaces an IDENTITY primary-key column <c>Id</c> with a plain <c>bigint</c> column holding
    /// the same values. Every foreign key referencing the column must be dropped beforehand and
    /// recreated afterwards by the caller.
    /// </summary>
    public static void RebuildIdAsBigintWithoutIdentity(
        this MigrationBuilder migrationBuilder, string schema, string table, string primaryKey)
    {
        migrationBuilder.AddColumn<long>(
            name: TempColumn,
            schema: schema,
            table: table,
            type: "bigint",
            nullable: true);

        // Wrapped in EXEC so the column added above is resolved at run time: a generated migration
        // script runs the whole migration as one batch, and SQL Server compiles a batch before the
        // ADD COLUMN has executed ("Invalid column name").
        migrationBuilder.Sql($"EXEC(N'UPDATE [{schema}].[{table}] SET [{TempColumn}] = CAST([Id] AS bigint);');");

        migrationBuilder.DropPrimaryKey(
            name: primaryKey,
            schema: schema,
            table: table);

        migrationBuilder.DropColumn(
            name: "Id",
            schema: schema,
            table: table);

        migrationBuilder.RenameColumn(
            name: TempColumn,
            schema: schema,
            table: table,
            newName: "Id");

        migrationBuilder.AlterColumn<long>(
            name: "Id",
            schema: schema,
            table: table,
            type: "bigint",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "bigint",
            oldNullable: true);

        migrationBuilder.AddPrimaryKey(
            name: primaryKey,
            schema: schema,
            table: table,
            column: "Id");
    }
}
