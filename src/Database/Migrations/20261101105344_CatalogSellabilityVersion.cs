using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <summary>
    /// A sequence the catalogue bumps whenever something sellable changes, so a cart can tell
    /// whether its contents are still the ones it was priced against.
    /// <para>
    /// Hand-written: EF has no model concept for a bare sequence that no column defaults from,
    /// so it scaffolded an empty migration. Carried in <c>Up</c> rather than a repeatable script
    /// because dropping it would lose the counter, and every holder of an old version number
    /// would then read as current.
    /// </para>
    /// </summary>
    public partial class CatalogSellabilityVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // A sequence, not a row: every sellability change has to bump it, and a row would
            // serialise all of them behind one lock. nextval takes no transaction and never
            // blocks; the gaps a rolled-back caller leaves do not matter, because the only
            // question asked of this number is whether it is the one seen before.
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS catalog.sellability_version AS bigint START WITH 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS catalog.sellability_version");
        }
    }
}
