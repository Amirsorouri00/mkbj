using EwaysVNext.Database.Catalog.Views;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <summary>
    /// Gives the storefront a real popularity column, now that there are sales to count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The phase 4 obligation ADR-0020 recorded. V1 shipped <c>popularity_30d</c> as a literal
    /// zero because <c>sales.order_lines</c> did not exist, and <c>mv_home</c> emitted no best
    /// sellers at all rather than ranking everything equally — an empty section is honest, a
    /// fabricated one is not.
    /// </para>
    /// <para>
    /// A materialized view cannot be altered, so both are dropped and recreated. Dropping takes
    /// their indexes with it silently, which is why the index SQL is reapplied from a named
    /// constant rather than remembered.
    /// </para>
    /// </remarks>
    public partial class StorefrontPopularityFromSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Home reads product, so it goes first.
            foreach (var view in StorefrontViewSql.DropOrder)
            {
                migrationBuilder.Sql($"DROP MATERIALIZED VIEW IF EXISTS {view}");
            }

            migrationBuilder.Sql(StorefrontViewSql.ProductV2);
            migrationBuilder.Sql(StorefrontViewSql.HomeV2);
            migrationBuilder.Sql(StorefrontViewSql.ProductAndHomeIndexes);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            foreach (var view in StorefrontViewSql.DropOrder)
            {
                migrationBuilder.Sql($"DROP MATERIALIZED VIEW IF EXISTS {view}");
            }

            migrationBuilder.Sql(StorefrontViewSql.ProductV1);
            migrationBuilder.Sql(StorefrontViewSql.HomeV1);
            migrationBuilder.Sql(StorefrontViewSql.ProductAndHomeIndexes);
        }
    }
}
