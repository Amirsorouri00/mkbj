namespace EwaysVNext.Modules.Catalog.QueryApplication.Storefront;

/// <summary>
/// The storefront's read queries, as SQL.
/// </summary>
/// <remarks>
/// <para>
/// Raw SQL rather than LINQ because the sources are materialized views that no <c>DbSet</c> maps,
/// and because the shapes here — array containment, ranked sections, aggregated facets — are the
/// reason those views exist.
/// </para>
/// <para>
/// Columns are named in snake_case, because the context uses the snake_case naming convention and
/// EF applies it to ad-hoc query types too: a column aliased <c>"BrandId"</c> is looked up as
/// <c>brand_id</c> and reported missing. Anything computed is given an explicit snake_case alias
/// rather than left to Postgres' default name.
/// </para>
/// <para>
/// Every read of the variant view repeats the <c>fx_valid_until</c> guard. A rate can expire
/// between two refreshes, and the row stays until the next one — showing a price the quote would
/// now refuse is exactly the drift the parity tests exist to prevent.
/// </para>
/// </remarks>
internal static class StorefrontSql
{
    /// <summary>
    /// A currency the seller does not sell in is not a 404 — it is a bad request.
    /// </summary>
    /// <remarks>
    /// Aliased <c>"Value"</c>, not snake_case: a scalar <c>SqlQueryRaw&lt;T&gt;</c> looks for that
    /// exact name and the naming convention does not touch it. The row types below are the other
    /// way round. Nothing but a test tells the two apart.
    /// </remarks>
    public const string CurrencyIsEnabled =
        """SELECT EXISTS (SELECT 1 FROM pricing.currencies WHERE code = @currency AND is_enabled) AS "Value" """;

    /// <summary>The ranked product sections, joined to the cards they name.</summary>
    public const string HomeProducts = """
        SELECT h.section AS section, p.product_id AS product_id, p.slug AS slug,
               p.title AS title, p.brand_id AS brand_id, p.brand_name AS brand_name,
               p.category_id AS category_id, p.min_price_minor AS min_price_minor,
               p.max_price_minor AS max_price_minor, p.variant_count AS variant_count,
               p.primary_image_key AS image_key
        FROM catalog.mv_home h
        JOIN catalog.mv_storefront_product p
          ON p.product_id = h.product_id AND p.currency = h.currency
        WHERE h.currency = @currency AND h.product_id IS NOT NULL
        ORDER BY h.section, h.rank
        """;

    /// <summary>
    /// The featured category tiles. The count comes from the view rather than the tile's rank, so
    /// a tile never claims more products than the storefront can show.
    /// </summary>
    public const string HomeCategories = """
        SELECT c.id AS category_id, c.name AS name, c.slug AS slug,
               (SELECT count(*) FROM catalog.mv_storefront_product p
                 WHERE p.currency = h.currency AND h.category_id = p.ancestor_category_ids[1])
                 AS product_count
        FROM catalog.mv_home h
        JOIN catalog.categories c ON c.id = h.category_id
        WHERE h.currency = @currency AND h.section = 'featured_categories'
        ORDER BY h.rank
        """;

    /// <summary>
    /// The product, if the storefront holds it in this currency. The descriptions come from the
    /// live row — they are long text that would bloat every listing row in the view — but the
    /// view decides whether the product exists at all.
    /// </summary>
    public const string ProductBySlug = """
        SELECT p.product_id AS product_id, p.slug AS slug, p.title AS title,
               p.brand_id AS brand_id, p.brand_name AS brand_name,
               p.ancestor_category_ids AS ancestor_category_ids, p.image_keys AS image_keys,
               live.short_description AS short_description, live.description AS description
        FROM catalog.mv_storefront_product p
        JOIN catalog.products live ON live.id = p.product_id
        WHERE p.slug = @slug AND p.currency = @currency
        """;

    public const string Breadcrumb = """
        SELECT c.id AS category_id, c.name AS name, c.slug AS slug
        FROM unnest(@ids::bigint[]) WITH ORDINALITY AS a(id, ord)
        JOIN catalog.categories c ON c.id = a.id
        ORDER BY a.ord
        """;

    public const string VariantOffers = """
        SELECT v.variant_id AS variant_id, v.sku AS sku, v.price_minor AS price_minor,
               v.attribute_values::text AS attribute_values
        FROM catalog.mv_storefront_variant v
        WHERE v.product_id = @productId AND v.currency = @currency
          AND (v.fx_valid_until IS NULL OR v.fx_valid_until >= now())
        ORDER BY v.price_minor, v.variant_id
        """;

    /// <summary>
    /// Every facet row for one category, in one currency.
    /// </summary>
    /// <remarks>
    /// The view already carries both halves of a brand facet: <c>facet_key</c> is the brand id, a
    /// filter sends that back, and <c>facet_value</c> is the display name a visitor reads. For an
    /// attribute the key is the attribute and the value is the value; for a price bucket the key
    /// is literally <c>bucket</c> and the value is its lower bound. No join is needed for any of
    /// them.
    /// </remarks>
    public const string Facets = """
        SELECT facet_kind, facet_key, facet_value, product_count
        FROM catalog.mv_category_facets
        WHERE category_id = @categoryId AND currency = @currency
        """;
}
