namespace EwaysVNext.Database.Catalog.Views;

/// <summary>
/// The storefront's read model: four materialized views that decide what a browsing visitor is
/// <b>shown</b>.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0020: nothing in a purchasing path may read these. Cart, checkout and composition go to
/// the live tables through <c>ISellableVariantPort</c> and <c>IPriceQuotePort</c>. A view is
/// stale by construction — it is refreshed on a schedule and after a change — and a basket
/// composed from a stale price is a basket that does not total what the partner asked for.
/// </para>
/// <para>
/// The grain of each view is enforced by a unique index, which is also what makes
/// <c>REFRESH MATERIALIZED VIEW CONCURRENTLY</c> legal. Without it every refresh would take an
/// <c>ACCESS EXCLUSIVE</c> lock and the shop would stall behind it.
/// </para>
/// <para>
/// Prices are computed here the same way <c>PriceResolver</c> computes them in C# — override,
/// then base-currency match, then conversion rounded by <c>pricing.round_half_even</c>. Postgres'
/// own <c>round()</c> is half-away-from-zero and would drift a cent away from the quote.
/// <c>StorefrontPriceParityTests</c> holds the two to each other.
/// </para>
/// </remarks>
public static class StorefrontViewSql
{
    /// <summary>
    /// Grain: variant × enabled currency. A row exists only where the catalogue says the variant
    /// is sellable <b>and</b> pricing can name a price in that currency.
    /// </summary>
    public const string VariantV1 = """
        CREATE MATERIALIZED VIEW catalog.mv_storefront_variant AS
        WITH RECURSIVE cat_tree AS (
            SELECT c.id, ARRAY[c.id]::bigint[] AS path_ids
            FROM catalog.categories c WHERE c.parent_id IS NULL AND c.status = 1
          UNION ALL
            SELECT c.id, t.path_ids || c.id
            FROM catalog.categories c JOIN cat_tree t ON c.parent_id = t.id WHERE c.status = 1
        ), eligible AS (
            SELECT v.id AS variant_id, p.id AS product_id, p.slug, v.sku, p.title,
                   lower(unaccent(p.title)) AS title_norm, b.id AS brand_id,
                   b.display_name AS brand_name,
                   p.primary_category_id AS category_id, t.path_ids AS ancestor_category_ids,
                   v.attribute_values, p.created_at,
                   (SELECT m.object_key FROM catalog.product_media m
                     WHERE m.product_id = p.id AND m.usage_rights = 'Allowed'
                     ORDER BY m.sort_order, m.id LIMIT 1) AS primary_image_key,
                   vp.id AS variant_price_id, vp.base_amount_minor, vp.base_currency
            FROM catalog.variants v
            JOIN catalog.products p ON p.id = v.product_id AND p.status = 'Active'
            JOIN catalog.brands b ON b.id = p.brand_id AND b.is_active
            JOIN cat_tree t ON t.id = p.primary_category_id
            JOIN pricing.variant_prices vp ON vp.variant_id = v.id
            WHERE v.is_active
        ), priced AS (
            SELECT e.*, cur.code AS currency,
                   CASE WHEN co.amount_minor IS NOT NULL THEN co.amount_minor
                        WHEN cur.code = e.base_currency THEN e.base_amount_minor
                        ELSE pricing.round_half_even(e.base_amount_minor * r.rate)::bigint END
                     AS price_minor,
                   CASE WHEN co.amount_minor IS NOT NULL THEN 'Override'
                        WHEN cur.code = e.base_currency THEN 'Base' ELSE 'Fx' END AS price_source,
                   CASE WHEN co.amount_minor IS NULL AND cur.code <> e.base_currency
                        THEN r.valid_until END AS fx_valid_until
            FROM eligible e
            CROSS JOIN pricing.currencies cur
            LEFT JOIN pricing.currency_overrides co
                   ON co.variant_price_id = e.variant_price_id AND co.currency = cur.code
                  AND co.price_list = 'default'
            LEFT JOIN pricing.resolved_fx_rates r
                   ON r.base = e.base_currency AND r.quote = cur.code
                  AND (r.valid_until IS NULL OR r.valid_until >= now())
            WHERE cur.is_enabled
        )
        SELECT variant_id, product_id, slug, sku, title, title_norm, brand_id, brand_name,
               category_id, ancestor_category_ids, attribute_values, primary_image_key, created_at,
               currency, price_minor, price_source, fx_valid_until,
               setweight(to_tsvector('simple', unaccent(title)), 'A')
            || setweight(to_tsvector('simple', unaccent(brand_name)), 'B')
            || setweight(to_tsvector('simple', unaccent(coalesce(
                   (SELECT string_agg(x.value, ' ') FROM jsonb_each_text(attribute_values) x), ''))), 'C')
               AS search
        FROM priced
        WHERE price_minor >= 1
        """;

    /// <summary>
    /// Grain: product × currency. A listing page shows products, not variants, so the price range
    /// and the variant count are aggregated here rather than in every query that reads them.
    /// </summary>
    public const string ProductV1 = """
        CREATE MATERIALIZED VIEW catalog.mv_storefront_product AS
        SELECT v.product_id, v.currency, min(v.slug) AS slug, min(v.title) AS title,
               min(v.title_norm) AS title_norm, min(v.brand_id) AS brand_id,
               min(v.brand_name) AS brand_name,
               min(v.category_id) AS category_id,
               min(v.ancestor_category_ids) AS ancestor_category_ids,
               min(v.price_minor) AS min_price_minor, max(v.price_minor) AS max_price_minor,
               count(*) AS variant_count, min(v.primary_image_key) AS primary_image_key,
               (SELECT coalesce(array_agg(m.object_key ORDER BY m.sort_order, m.id), '{}')
                  FROM catalog.product_media m
                 WHERE m.product_id = v.product_id AND m.usage_rights = 'Allowed') AS image_keys,
               (SELECT coalesce(jsonb_object_agg(f.key, f.vals), '{}'::jsonb)
                  FROM (SELECT kv.key, jsonb_agg(DISTINCT kv.value ORDER BY kv.value) AS vals
                          FROM catalog.mv_storefront_variant v2, jsonb_each_text(v2.attribute_values) kv
                         WHERE v2.product_id = v.product_id AND v2.currency = v.currency
                         GROUP BY kv.key) f) AS facet_values,
               0::bigint AS popularity_30d,
               min(v.created_at) AS created_at
        FROM catalog.mv_storefront_variant v
        GROUP BY v.product_id, v.currency
        """;

    /// <summary>
    /// Grain: category × currency × kind × key × value, where category <c>0</c> is the whole
    /// catalogue.
    /// </summary>
    /// <remarks>
    /// Attribute facets appear only for keys with a live <c>is_filterable</c> assignment somewhere
    /// on the variant's category path. Per-category exclusions are <b>not</b> applied — a known
    /// limitation, recorded here rather than left for a reader to discover from the absence of a
    /// join.
    /// </remarks>
    public const string FacetsV1 = """
        CREATE MATERIALIZED VIEW catalog.mv_category_facets AS
        WITH scoped AS (
            SELECT s.category_id, v.product_id, v.currency, v.price_minor, v.brand_id, v.brand_name,
                   v.attribute_values, v.ancestor_category_ids
            FROM catalog.mv_storefront_variant v
            CROSS JOIN LATERAL unnest(v.ancestor_category_ids || 0::bigint) AS s(category_id)
        )
        SELECT sc.category_id, sc.currency, 'attribute'::text AS facet_kind, kv.key AS facet_key,
               kv.value AS facet_value, count(DISTINCT sc.product_id) AS product_count
        FROM scoped sc CROSS JOIN LATERAL jsonb_each_text(sc.attribute_values) kv
        WHERE EXISTS (SELECT 1 FROM catalog.category_attribute_assignments a
                      JOIN catalog.attribute_blueprints ab ON ab.id = a.attribute_blueprint_id
                      WHERE ab.canonical_key = kv.key AND a.is_filterable AND a.removed_at IS NULL
                        AND a.category_id = ANY (sc.ancestor_category_ids))
        GROUP BY sc.category_id, sc.currency, kv.key, kv.value
        UNION ALL
        SELECT category_id, currency, 'brand', brand_id::text, min(brand_name),
               count(DISTINCT product_id)
        FROM scoped GROUP BY category_id, currency, brand_id
        UNION ALL
        SELECT category_id, currency, 'price', 'bucket', b.lower_minor::text,
               count(DISTINCT product_id)
        FROM scoped CROSS JOIN LATERAL (SELECT CASE
                WHEN price_minor < 1000 THEN 0 WHEN price_minor < 5000 THEN 1000
                WHEN price_minor < 10000 THEN 5000 WHEN price_minor < 50000 THEN 10000
                WHEN price_minor < 100000 THEN 50000 WHEN price_minor < 500000 THEN 100000
                WHEN price_minor < 1000000 THEN 500000 ELSE 1000000 END AS lower_minor) b
        GROUP BY category_id, currency, b.lower_minor
        """;

    /// <summary>
    /// Grain: section × currency × rank.
    /// </summary>
    /// <remarks>
    /// <c>popularity_30d</c> is <c>0</c> until the sales tables exist in phase 4, so
    /// <c>best_sellers</c> emits no rows at all rather than ranking everything equally. An empty
    /// section is honest; a fabricated one is not.
    /// </remarks>
    public const string HomeV1 = """
        CREATE MATERIALIZED VIEW catalog.mv_home AS
        SELECT 'new_arrivals'::text AS section, x.currency, x.rank, x.product_id,
               NULL::bigint AS category_id
        FROM (SELECT currency, product_id,
                     row_number() OVER (PARTITION BY currency
                                        ORDER BY created_at DESC, product_id DESC) AS rank
              FROM catalog.mv_storefront_product) x WHERE x.rank <= 12
        UNION ALL
        SELECT 'best_sellers', x.currency, x.rank, x.product_id, NULL
        FROM (SELECT currency, product_id,
                     row_number() OVER (PARTITION BY currency
                                        ORDER BY popularity_30d DESC, product_id DESC) AS rank
              FROM catalog.mv_storefront_product WHERE popularity_30d > 0) x WHERE x.rank <= 12
        UNION ALL
        SELECT 'featured_categories', x.currency, x.rank, NULL, x.category_id
        FROM (SELECT p.currency, p.ancestor_category_ids[1] AS category_id,
                     row_number() OVER (PARTITION BY p.currency
                                        ORDER BY count(*) DESC, p.ancestor_category_ids[1]) AS rank
              FROM catalog.mv_storefront_product p
              GROUP BY p.currency, p.ancestor_category_ids[1]) x WHERE x.rank <= 8
        """;

    /// <summary>
    /// The grain indexes concurrent refresh requires, plus the read paths the browse API takes:
    /// full-text search, trigram prefix matching, ancestry containment, and price and recency
    /// ordering within a currency.
    /// </summary>
    public const string IndexesV1 = """
        CREATE UNIQUE INDEX ux_mv_sv_grain ON catalog.mv_storefront_variant (variant_id, currency);
        CREATE INDEX ix_mv_sv_search ON catalog.mv_storefront_variant USING gin (search);
        CREATE INDEX ix_mv_sv_ancestors ON catalog.mv_storefront_variant USING gin (ancestor_category_ids);
        CREATE INDEX ix_mv_sv_title_trgm ON catalog.mv_storefront_variant USING gin (title_norm gin_trgm_ops);
        CREATE INDEX ix_mv_sv_currency_price ON catalog.mv_storefront_variant (currency, price_minor);
        CREATE INDEX ix_mv_sv_currency_created ON catalog.mv_storefront_variant (currency, created_at);
        CREATE INDEX ix_mv_sv_product ON catalog.mv_storefront_variant (product_id, currency);
        CREATE UNIQUE INDEX ux_mv_sp_grain ON catalog.mv_storefront_product (product_id, currency);
        CREATE UNIQUE INDEX ux_mv_sp_slug ON catalog.mv_storefront_product (slug, currency);
        CREATE INDEX ix_mv_sp_ancestors ON catalog.mv_storefront_product USING gin (ancestor_category_ids);
        CREATE INDEX ix_mv_sp_title_trgm ON catalog.mv_storefront_product USING gin (title_norm gin_trgm_ops);
        CREATE INDEX ix_mv_sp_currency_price ON catalog.mv_storefront_product (currency, min_price_minor);
        CREATE INDEX ix_mv_sp_currency_created ON catalog.mv_storefront_product (currency, created_at);
        CREATE UNIQUE INDEX ux_mv_cf_grain ON catalog.mv_category_facets
            (category_id, currency, facet_kind, facet_key, facet_value);
        CREATE UNIQUE INDEX ux_mv_home_grain ON catalog.mv_home (section, currency, rank);
        """;

    /// <summary>
    /// Refreshing a materialized view requires owning it, and the application owns nothing. This
    /// function is owned by the migrator and executes as its owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name is checked against <see cref="RefreshOrder"/> inside the function rather than
    /// trusted. A <c>SECURITY DEFINER</c> function that interpolates an unchecked identifier is a
    /// privilege escalation: the caller owns nothing, and the function owns the schema.
    /// <c>search_path</c> is pinned for the same reason.
    /// </para>
    /// <para>
    /// The parameter is <c>use_concurrently</c> because <c>concurrently</c> is a reserved word —
    /// plpgsql validates the body at <c>CREATE</c> time and refuses it, with a position inside
    /// the body that reads like a truncated statement.
    /// </para>
    /// <para>
    /// A failure is raised, not swallowed, so the caller sees <c>55000</c> (not yet populated)
    /// with its SQLSTATE intact and can decide whether a blocking refresh is acceptable.
    /// </para>
    /// </remarks>
    public const string RefreshFunctionV1 = """
        CREATE OR REPLACE FUNCTION catalog.refresh_storefront_view(
            view_name text, use_concurrently boolean DEFAULT true)
        RETURNS void
        LANGUAGE plpgsql
        SECURITY DEFINER
        SET search_path = pg_catalog, public
        AS $$
        BEGIN
            IF view_name NOT IN (
                'catalog.mv_storefront_variant', 'catalog.mv_storefront_product',
                'catalog.mv_category_facets', 'catalog.mv_home')
            THEN
                RAISE EXCEPTION 'refresh_storefront_view: % is not a storefront view', view_name
                    USING ERRCODE = 'insufficient_privilege';
            END IF;

            IF use_concurrently THEN
                EXECUTE 'REFRESH MATERIALIZED VIEW CONCURRENTLY ' || view_name;
            ELSE
                EXECUTE 'REFRESH MATERIALIZED VIEW ' || view_name;
            END IF;
        END
        $$;
        """;

    /// <summary>
    /// The order a full refresh runs in: each view reads the one before it, so refreshing out of
    /// order would project yesterday's variants into today's products.
    /// </summary>
    public static readonly IReadOnlyList<string> RefreshOrder =
    [
        "catalog.mv_storefront_variant",
        "catalog.mv_storefront_product",
        "catalog.mv_category_facets",
        "catalog.mv_home",
    ];
}
