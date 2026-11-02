using System.Globalization;
using System.Text.Json;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Storefront;

/// <summary>
/// Reads the storefront views and shapes them for a browsing visitor.
/// </summary>
/// <remarks>
/// Read-only by design (ADR-0016): no aggregate is loaded, no rule is applied, nothing is
/// written. The only judgement here is whether the currency is one the seller sells in — which
/// is a fact about the request, not about any product.
/// </remarks>
public sealed class StorefrontQueries(
    EwaysVNextDbContext db,
    ICategoryQueries categories,
    IOptions<StorefrontOptions> options) : IStorefrontQueries
{
    private const int MaxFeaturedCategories = 8;

    /// <summary>A page a visitor did not ask a size for.</summary>
    private const int DefaultLimit = 24;

    /// <summary>Beyond this, the caller is scraping rather than browsing.</summary>
    private const int MaxLimit = 48;

    private const int MaxSearchLength = 200;
    private const int MaxAttributeKeys = 20;
    private const int MaxBrandIds = 50;

    private static readonly Error CurrencyNotEnabled = new(
        "CURRENCY_NOT_ENABLED", "The shop does not sell in that currency.", 422);

    private static readonly Error NotFound = new(
        "NOT_FOUND", "No such product.", 404);

    public async Task<Result<HomeDto>> GetHomeAsync(string currency, CancellationToken ct = default)
    {
        var code = Normalize(currency);
        if (!await IsEnabledAsync(code, ct)) return Result.Failure<HomeDto>(CurrencyNotEnabled);

        var products = await db.Database
            .SqlQueryRaw<HomeProductRow>(StorefrontSql.HomeProducts, Text("currency", code))
            .ToListAsync(ct);

        var tiles = await db.Database
            .SqlQueryRaw<HomeCategoryRow>(StorefrontSql.HomeCategories, Text("currency", code))
            .ToListAsync(ct);

        return Result.Success(new HomeDto(
            code,
            [.. products.Where(row => row.Section == "new_arrivals").Select(row => Card(row, code))],
            [.. products.Where(row => row.Section == "best_sellers").Select(row => Card(row, code))],
            [.. tiles.Take(MaxFeaturedCategories)
                .Select(row => new CategoryTileDto(row.CategoryId, row.Name, row.Slug, row.ProductCount))]));
    }

    public Task<IReadOnlyList<CategoryTreeNodeDto>> GetCategoryTreeAsync(CancellationToken ct = default) =>
        categories.GetTreeAsync(ct);

    public async Task<Result<ProductDetailDto>> GetProductBySlugAsync(
        string slug, string currency, CancellationToken ct = default)
    {
        var code = Normalize(currency);
        if (!await IsEnabledAsync(code, ct)) return Result.Failure<ProductDetailDto>(CurrencyNotEnabled);

        var product = await db.Database
            .SqlQueryRaw<ProductDetailRow>(
                StorefrontSql.ProductBySlug, Text("slug", slug ?? string.Empty), Text("currency", code))
            .FirstOrDefaultAsync(ct);

        // Absent from the view is absent from the shop, whatever the live table holds: a Draft
        // product, a deactivated one and a misspelled slug are the same answer to a visitor.
        if (product is null) return Result.Failure<ProductDetailDto>(NotFound);

        var breadcrumb = await db.Database
            .SqlQueryRaw<CrumbRow>(
                StorefrontSql.Breadcrumb,
                new NpgsqlParameter("ids", product.AncestorCategoryIds))
            .ToListAsync(ct);

        var offers = await db.Database
            .SqlQueryRaw<VariantOfferRow>(
                StorefrontSql.VariantOffers,
                new NpgsqlParameter("productId", product.ProductId), Text("currency", code))
            .ToListAsync(ct);

        var variants = offers
            .Select(row => new VariantOfferDto(
                row.VariantId, row.Sku, Attributes(row.AttributeValues), Money(row.PriceMinor, code)))
            .ToList();

        return Result.Success(new ProductDetailDto(
            product.ProductId,
            product.Slug,
            product.Title,
            product.ShortDescription,
            product.Description,
            product.BrandId,
            product.BrandName,
            [.. breadcrumb.Select(row => new CategoryCrumbDto(row.CategoryId, row.Name, row.Slug))],
            [.. product.ImageKeys.Select(Url)!],
            variants,
            AttributeOptions(variants)));
    }


    public async Task<Result<ProductPageDto>> GetProductsAsync(
        ProductListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var code = Normalize(query.Currency);
        if (!await IsEnabledAsync(code, ct)) return Result.Failure<ProductPageDto>(CurrencyNotEnabled);

        var validation = Validate(query);
        if (validation is not null) return Result.Failure<ProductPageDto>(validation);

        var searching = !string.IsNullOrWhiteSpace(query.Q);
        var sort = query.Sort ?? (searching ? StorefrontSort.Relevance : StorefrontSort.Newest);
        var limit = query.Limit ?? DefaultLimit;

        StorefrontCursor.TryDecode(query.Cursor, out var cursor);

        var page = await PageAsync(
            query, code, cursor?.Mode ?? StorefrontCursor.FtsMode, sort, cursor, limit, ct);

        // Only on the first page of a search. Paging into a trigram result set keeps its own mode
        // in the cursor, and falling back half way through would answer a different question than
        // the page before it.
        if (page.Items.Count == 0 && searching && cursor is null)
        {
            var fallback = await PageAsync(
                query, code, StorefrontCursor.TrigramMode, sort, null, limit, ct);

            if (fallback.Items.Count > 0)
                return Result.Success(fallback with { UsedTypoFallback = true });
        }

        return Result.Success(page);
    }

    public Task<Result<ProductPageDto>> SearchAsync(
        string q, string currency, string? cursor, int? limit, CancellationToken ct = default) =>
        GetProductsAsync(
            new ProductListQuery(currency, null, null, null, null, null, q, null, cursor, limit), ct);

    public async Task<Result<FacetSetDto>> GetFacetsAsync(
        long? categoryId, string currency, CancellationToken ct = default)
    {
        var code = Normalize(currency);
        if (!await IsEnabledAsync(code, ct)) return Result.Failure<FacetSetDto>(CurrencyNotEnabled);

        // 0 is the whole catalogue, which the view carries as a row like any other category.
        var rows = await db.Database
            .SqlQueryRaw<FacetRow>(
                StorefrontSql.Facets,
                new NpgsqlParameter("categoryId", categoryId ?? 0L), Text("currency", code))
            .ToListAsync(ct);

        var attributes = rows
            .Where(row => row.FacetKind == "attribute")
            .GroupBy(row => row.FacetKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new FacetGroupDto(
                group.Key, [.. Values(group, row => row.FacetValue)]))
            .ToList();

        // A brand filters by id and reads as a name, so the two halves differ; an attribute value
        // is both at once.
        return Result.Success(new FacetSetDto(
            attributes,
            [.. Values(rows.Where(row => row.FacetKind == "brand"), row => row.FacetKey)],
            Buckets(rows, code)));
    }

    /// <summary>Most products first, then alphabetically, so the order never depends on the plan.</summary>
    private static IEnumerable<FacetValueDto> Values(
        IEnumerable<FacetRow> rows, Func<FacetRow, string> value) =>
        rows.OrderByDescending(row => row.ProductCount)
            .ThenBy(row => row.FacetValue, StringComparer.Ordinal)
            .Select(row => new FacetValueDto(value(row), row.FacetValue, row.ProductCount));

    /// <summary>
    /// Each bucket's upper bound is the next bucket's lower one, so the ranges meet rather than
    /// leaving a gap a price could fall into. The top bucket is open-ended.
    /// </summary>
    private static List<PriceBucketDto> Buckets(IReadOnlyList<FacetRow> rows, string currency)
    {
        var ordered = rows
            .Where(row => row.FacetKind == "price")
            .Select(row => (
                Lower: long.Parse(row.FacetValue, CultureInfo.InvariantCulture), row.ProductCount))
            .OrderBy(bucket => bucket.Lower)
            .ToList();

        return [.. ordered.Select((bucket, index) => new PriceBucketDto(
            Money(bucket.Lower, currency),
            index + 1 < ordered.Count ? Money(ordered[index + 1].Lower, currency) : null,
            bucket.ProductCount))];
    }

    private async Task<ProductPageDto> PageAsync(
        ProductListQuery query, string currency, string mode, StorefrontSort sort,
        StorefrontCursor? cursor, int limit, CancellationToken ct)
    {
        var builder = new ProductPageSqlBuilder(query, currency, mode);
        var sql = builder.Build(sort, cursor, limit);

        var rows = await db.Database
            .SqlQueryRaw<ProductPageRow>(sql, builder.Parameters).ToListAsync(ct);

        // The extra row is the evidence that a next page exists; it is never shown.
        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).ToList();

        var next = hasMore && items.Count > 0
            ? new StorefrontCursor(mode, sort, KeyOf(items[^1], sort), items[^1].ProductId).Encode()
            : null;

        return new ProductPageDto(
            [.. items.Select(row => new ProductCardDto(
                row.ProductId, row.Slug, row.Title, row.BrandId, row.BrandName, row.CategoryId,
                Money(row.MinPriceMinor, currency), Money(row.MaxPriceMinor, currency),
                row.VariantCount, Url(row.ImageKey)))],
            next,
            UsedTypoFallback: false);
    }

    /// <summary>
    /// The last row's sort key, as text, invariantly — the same text the builder parses back. A
    /// round trip through the current culture would move a decimal point.
    /// </summary>
    private static string KeyOf(ProductPageRow row, StorefrontSort sort) => sort switch
    {
        StorefrontSort.PriceAsc or StorefrontSort.PriceDesc =>
            row.MinPriceMinor.ToString(CultureInfo.InvariantCulture),
        StorefrontSort.Newest => row.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        StorefrontSort.Popular => row.Popularity30d.ToString(CultureInfo.InvariantCulture),
        _ => row.Rank.ToString("R", CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// What the shop refuses before touching the database. Every one of these is a client bug,
    /// and answering it with a plausible page would hide it.
    /// </summary>
    private static Error? Validate(ProductListQuery query)
    {
        var limit = query.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
            return Validation($"limit must be between 1 and {MaxLimit}.");

        if (query.Q is { Length: > MaxSearchLength })
            return Validation($"q must be at most {MaxSearchLength} characters.");

        if (query.PriceMinMinor is { } min && query.PriceMaxMinor is { } max && min > max)
            return Validation("priceMin must not be greater than priceMax.");

        if (query.Sort == StorefrontSort.Relevance && string.IsNullOrWhiteSpace(query.Q))
            return Validation("relevance needs a search term to be relevant to.");

        if (query.Attributes is { Count: > MaxAttributeKeys })
            return Validation($"at most {MaxAttributeKeys} attribute filters.");

        if (query.BrandIds is { Count: > MaxBrandIds })
            return Validation($"at most {MaxBrandIds} brand filters.");

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            if (!StorefrontCursor.TryDecode(query.Cursor, out var cursor))
                return Validation("that cursor is not one this shop issued.");

            // A cursor is a position in one ordering. Replaying it against another would page
            // from a key that means nothing there, skipping or repeating products silently.
            var sort = query.Sort ?? (string.IsNullOrWhiteSpace(query.Q)
                ? StorefrontSort.Newest : StorefrontSort.Relevance);

            if (cursor!.Sort != sort) return Validation("that cursor belongs to a different sort.");
        }

        return null;
    }

    private static Error Validation(string message) => new("VALIDATION_FAILED", message, 400);

    /// <summary>
    /// The distinct values each attribute takes across the product's offers, sorted — what a
    /// selector on the page offers.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> AttributeOptions(
        IReadOnlyList<VariantOfferDto> variants) =>
        variants
            .SelectMany(variant => variant.Attributes)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)[.. group.Select(pair => pair.Value)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                StringComparer.Ordinal);

    private async Task<bool> IsEnabledAsync(string currency, CancellationToken ct) =>
        await db.Database
            .SqlQueryRaw<bool>(StorefrontSql.CurrencyIsEnabled, Text("currency", currency))
            .SingleAsync(ct);

    private ProductCardDto Card(HomeProductRow row, string currency) => new(
        row.ProductId, row.Slug, row.Title, row.BrandId, row.BrandName, row.CategoryId,
        Money(row.MinPriceMinor, currency), Money(row.MaxPriceMinor, currency),
        row.VariantCount, Url(row.ImageKey));

    /// <summary>Minor units to a two-decimal string, invariantly.</summary>
    private static MoneyDto Money(long minor, string currency) => new(
        (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture), currency);

    private string? Url(string? objectKey) =>
        objectKey is null ? null : options.Value.MediaBaseUrl + objectKey;

    private static Dictionary<string, string> Attributes(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    /// <summary>
    /// Currency codes are <c>char(3)</c>, so a value read back carries padding and a value sent
    /// in may carry whitespace. Comparing them anywhere else means comparing padding.
    /// </summary>
    private static string Normalize(string? currency) =>
        (currency ?? string.Empty).Trim().ToUpperInvariant();

    private static NpgsqlParameter Text(string name, string value) => new(name, value);

    private sealed class ProductPageRow
    {
        public long ProductId { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public long BrandId { get; init; }
        public string BrandName { get; init; } = string.Empty;
        public long CategoryId { get; init; }
        public long MinPriceMinor { get; init; }
        public long MaxPriceMinor { get; init; }
        public int VariantCount { get; init; }
        public string? ImageKey { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public long Popularity30d { get; init; }
        public float Rank { get; init; }
    }

    private sealed class FacetRow
    {
        public string FacetKind { get; init; } = string.Empty;
        public string FacetKey { get; init; } = string.Empty;
        public string FacetValue { get; init; } = string.Empty;
        public long ProductCount { get; init; }
    }

    private sealed class HomeProductRow
    {
        public string Section { get; init; } = string.Empty;
        public long ProductId { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public long BrandId { get; init; }
        public string BrandName { get; init; } = string.Empty;
        public long CategoryId { get; init; }
        public long MinPriceMinor { get; init; }
        public long MaxPriceMinor { get; init; }
        public int VariantCount { get; init; }
        public string? ImageKey { get; init; }
    }

    private sealed class HomeCategoryRow
    {
        public long CategoryId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public long ProductCount { get; init; }
    }

    private sealed class ProductDetailRow
    {
        public long ProductId { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public long BrandId { get; init; }
        public string BrandName { get; init; } = string.Empty;
        public long[] AncestorCategoryIds { get; init; } = [];
        public string[] ImageKeys { get; init; } = [];
        public string? ShortDescription { get; init; }
        public string? Description { get; init; }
    }

    private sealed class CrumbRow
    {
        public long CategoryId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
    }

    private sealed class VariantOfferRow
    {
        public long VariantId { get; init; }
        public string Sku { get; init; } = string.Empty;
        public long PriceMinor { get; init; }
        public string AttributeValues { get; init; } = "{}";
    }
}
