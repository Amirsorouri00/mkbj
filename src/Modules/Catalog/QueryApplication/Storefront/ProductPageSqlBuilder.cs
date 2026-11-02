using System.Globalization;
using System.Text;
using Npgsql;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Storefront;

/// <summary>
/// Builds one listing query: the filters that were asked for, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The clauses are composed rather than written once with <c>(@x IS NULL OR …)</c> for every
/// filter, because that form makes the planner choose a plan for the general case and ignore the
/// GIN indexes the views carry. A query with no filters should read no index conditions at all.
/// </para>
/// <para>
/// Every value is a parameter. Attribute <b>keys</b> are parameters too (<c>attribute_values ->>
/// @k0</c>), so nothing a visitor types is ever concatenated into SQL. The only text this class
/// interpolates is its own clause templates and parameter names it generated itself.
/// </para>
/// </remarks>
internal sealed class ProductPageSqlBuilder
{
    private readonly List<NpgsqlParameter> _parameters = [];
    private readonly List<string> _conditions = [];
    private readonly ProductListQuery _query;
    private readonly string _mode;
    private readonly string _currency;

    public ProductPageSqlBuilder(ProductListQuery query, string currency, string mode)
    {
        _query = query;
        _currency = currency;
        _mode = mode;
    }

    public NpgsqlParameter[] Parameters => [.. _parameters];

    /// <summary>
    /// The full statement. <paramref name="cursor"/> is the row the previous page stopped at, and
    /// <paramref name="limit"/> is the page size — one more row than that is fetched, because the
    /// cheapest way to know whether a next page exists is to look at one row of it.
    /// </summary>
    public string Build(StorefrontSort sort, StorefrontCursor? cursor, int limit)
    {
        var searching = !string.IsNullOrWhiteSpace(_query.Q);
        var trigram = _mode == StorefrontCursor.TrigramMode;

        Add("currency", _currency);
        _conditions.Add("v.currency = @currency");

        // A rate can expire between two refreshes, and the row stays until the next one.
        _conditions.Add("(v.fx_valid_until IS NULL OR v.fx_valid_until >= now())");

        if (_query.CategoryId is { } categoryId)
        {
            // Containment, so a category matches its whole subtree — and it is the shape the GIN
            // index on ancestor_category_ids answers.
            Add("category_id", categoryId);
            _conditions.Add("v.ancestor_category_ids @> ARRAY[@category_id]::bigint[]");
        }

        if (_query.BrandIds is { Count: > 0 } brandIds)
        {
            Add("brand_ids", brandIds.ToArray());
            _conditions.Add("v.brand_id = ANY(@brand_ids)");
        }

        // All attribute filters are applied to the SAME variant row: "Red and size M" means one
        // variant that is both, not a product with a red variant and a medium one.
        var attributeIndex = 0;
        foreach (var (attributeKey, values) in _query.Attributes ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            if (values is not { Count: > 0 }) continue;

            var n = attributeIndex.ToString(CultureInfo.InvariantCulture);
            Add($"k{n}", attributeKey);
            Add($"v{n}", values.ToArray());
            _conditions.Add($"(v.attribute_values ->> @k{n}) = ANY(@v{n})");
            attributeIndex++;
        }

        if (_query.PriceMinMinor is { } min)
        {
            Add("price_min", min);
            _conditions.Add("v.price_minor >= @price_min");
        }

        if (_query.PriceMaxMinor is { } max)
        {
            Add("price_max", max);
            _conditions.Add("v.price_minor <= @price_max");
        }

        if (searching)
        {
            Add("q", _query.Q!);
            _conditions.Add(trigram
                ? "v.title_norm % lower(unaccent(@q))"
                : "v.search @@ q.query");
        }

        var sql = new StringBuilder();

        if (searching && !trigram)
            sql.AppendLine("WITH q AS (SELECT websearch_to_tsquery('simple', unaccent(@q)) AS query)");

        sql.AppendLine(searching && !trigram ? ", matches AS (" : "WITH matches AS (");
        sql.AppendLine("    SELECT v.product_id, min(v.price_minor) AS price_minor,");
        sql.AppendLine(RankExpression(searching, trigram));
        sql.Append("    FROM catalog.mv_storefront_variant v");
        sql.AppendLine(searching && !trigram ? " CROSS JOIN q" : string.Empty);
        sql.AppendLine("    WHERE " + string.Join("\n      AND ", _conditions));
        sql.AppendLine("    GROUP BY v.product_id)");

        sql.AppendLine("""
            SELECT p.product_id, p.slug, p.title, p.brand_id, p.brand_name, p.category_id,
                   m.price_minor AS min_price_minor, p.max_price_minor,
                   p.variant_count, p.primary_image_key AS image_key,
                   p.created_at, m.rank,
                   -- Aliased, because the snake_case convention turns the row property
                   -- Popularity30d into popularity30d and would not find popularity_30d.
                   p.popularity_30d AS popularity30d
            FROM matches m
            JOIN catalog.mv_storefront_product p
              ON p.product_id = m.product_id AND p.currency = @currency
            """);

        var key = SortKey(sort);
        if (cursor is not null) sql.AppendLine("WHERE " + Keyset(sort, key, cursor));

        sql.AppendLine(CultureInfo.InvariantCulture,
            $"ORDER BY {key} {(Descending(sort) ? "DESC" : "ASC")}, p.product_id ASC");

        Add("limit_plus_one", limit + 1);
        sql.Append("LIMIT @limit_plus_one");

        return sql.ToString();
    }

    /// <summary>
    /// The rank each mode scores by. Without a search term there is nothing to rank, and a
    /// constant keeps the column present so the row shape never changes.
    /// </summary>
    private static string RankExpression(bool searching, bool trigram) =>
        searching
            ? trigram
                ? "           max(similarity(v.title_norm, lower(unaccent(@q)))) AS rank"
                : "           max(ts_rank(v.search, q.query)) AS rank"
            : "           0::real AS rank";

    /// <summary>
    /// The card's minimum price is the <b>matched</b> variants' minimum — filter to blue and the
    /// price shown is the blue one — while the maximum is the product's, so a range reads as the
    /// product's range rather than one variant's.
    /// </summary>
    private static string SortKey(StorefrontSort sort) => sort switch
    {
        StorefrontSort.PriceAsc or StorefrontSort.PriceDesc => "m.price_minor",
        StorefrontSort.Newest => "p.created_at",
        StorefrontSort.Popular => "p.popularity_30d",
        StorefrontSort.Relevance => "m.rank",
        _ => throw new ArgumentOutOfRangeException(nameof(sort)),
    };

    private static bool Descending(StorefrontSort sort) =>
        sort is StorefrontSort.PriceDesc or StorefrontSort.Newest
            or StorefrontSort.Popular or StorefrontSort.Relevance;

    /// <summary>
    /// "Strictly after the cursor's row" in the sort's own direction, with the product id
    /// breaking ties ascending either way.
    /// </summary>
    private string Keyset(StorefrontSort sort, string key, StorefrontCursor cursor)
    {
        AddKey(sort, cursor.Key);
        Add("cursor_id", cursor.ProductId);

        return Descending(sort)
            ? $"({key} < @cursor_key OR ({key} = @cursor_key AND p.product_id > @cursor_id))"
            : $"({key}, p.product_id) > (@cursor_key, @cursor_id)";
    }

    /// <summary>
    /// The key is carried as text, because one cursor type serves four differently typed sort
    /// keys. It is parsed back to the column's type here rather than cast in SQL, where a cast
    /// around the column would stop the index being used.
    /// </summary>
    private void AddKey(StorefrontSort sort, string key)
    {
        switch (sort)
        {
            case StorefrontSort.PriceAsc or StorefrontSort.PriceDesc:
                Add("cursor_key", long.Parse(key, CultureInfo.InvariantCulture));
                break;
            case StorefrontSort.Newest:
                Add("cursor_key", DateTimeOffset.Parse(key, CultureInfo.InvariantCulture));
                break;
            case StorefrontSort.Popular:
                Add("cursor_key", long.Parse(key, CultureInfo.InvariantCulture));
                break;
            default:
                Add("cursor_key", float.Parse(key, CultureInfo.InvariantCulture));
                break;
        }
    }

    private void Add(string name, object value) => _parameters.Add(new NpgsqlParameter(name, value));
}
