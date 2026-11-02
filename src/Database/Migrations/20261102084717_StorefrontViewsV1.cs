using EwaysVNext.Database.Catalog.Views;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EwaysVNext.Database.Migrations
{
    /// <summary>
    /// The storefront read model. The SQL lives in <see cref="StorefrontViewSql"/> so the
    /// refresher and the parity tests read the same text this migration applied.
    /// </summary>
    /// <remarks>
    /// The views are created <c>WITH DATA</c>, which is the default, so the very first
    /// <c>REFRESH … CONCURRENTLY</c> is legal — a view created <c>WITH NO DATA</c> cannot be
    /// refreshed concurrently until it has been refreshed some other way first.
    /// </remarks>
    public partial class StorefrontViewsV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql(StorefrontViewSql.VariantV1);
            migrationBuilder.Sql(StorefrontViewSql.ProductV1);
            migrationBuilder.Sql(StorefrontViewSql.FacetsV1);
            migrationBuilder.Sql(StorefrontViewSql.HomeV1);
            migrationBuilder.Sql(StorefrontViewSql.IndexesV1);
            migrationBuilder.Sql(StorefrontViewSql.RefreshFunctionV1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS catalog.refresh_storefront_view(text, boolean)");

            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS catalog.mv_home");
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS catalog.mv_category_facets");
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS catalog.mv_storefront_product");
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS catalog.mv_storefront_variant");
        }
    }
}
