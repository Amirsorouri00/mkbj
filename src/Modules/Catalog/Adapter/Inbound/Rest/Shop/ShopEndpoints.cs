using System.Globalization;
using EwaysVNext.Modules.Catalog.QueryApplication.Storefront;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Money;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Shop;

/// <summary>
/// The storefront a visitor browses before signing in to anything.
/// </summary>
/// <remarks>
/// <para>
/// Every route is anonymous on purpose. A shop that demands a token to show a product page is a
/// shop nobody indexes and nobody shares a link to.
/// </para>
/// <para>
/// The whole surface reads the materialized views (ADR-0020). Nothing here may be used to price a
/// basket: these answers are stale by construction, and the cart reads the live tables.
/// </para>
/// </remarks>
public sealed class ShopEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/shop";

    /// <summary>Query keys shaped <c>attr.colour=Red</c> become one attribute filter.</summary>
    private const string AttributePrefix = "attr.";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath).WithTags("Shop");

        group.MapGet("/home", async (
                string? currency, IStorefrontQueries queries, CancellationToken ct) =>
                (await queries.GetHomeAsync(currency ?? string.Empty, ct)).ToHttpResult())
            .AllowAnonymous();

        group.MapGet("/categories", async (IStorefrontQueries queries, CancellationToken ct) =>
                Result.Success(await queries.GetCategoryTreeAsync(ct)).ToHttpResult())
            .AllowAnonymous();

        group.MapGet("/products", async (
                HttpRequest request, IStorefrontQueries queries, CancellationToken ct) =>
            {
                var query = ReadListQuery(request);
                return query.IsFailure
                    ? Result.Failure(query.Error!).ToHttpResult()
                    : (await queries.GetProductsAsync(query.Value!, ct)).ToHttpResult();
            })
            .AllowAnonymous();

        group.MapGet("/products/{slug}", async (
                string slug, string? currency, IStorefrontQueries queries, CancellationToken ct) =>
                (await queries.GetProductBySlugAsync(slug, currency ?? string.Empty, ct)).ToHttpResult())
            .AllowAnonymous();

        group.MapGet("/facets", async (
                string? currency, long? categoryId, IStorefrontQueries queries, CancellationToken ct) =>
                (await queries.GetFacetsAsync(categoryId, currency ?? string.Empty, ct)).ToHttpResult())
            .AllowAnonymous();

        group.MapGet("/search", async (
                string? q, string? currency, string? cursor, int? limit,
                IStorefrontQueries queries, CancellationToken ct) =>
                (await queries.SearchAsync(q ?? string.Empty, currency ?? string.Empty, cursor, limit, ct))
                    .ToHttpResult())
            .AllowAnonymous();
    }

    /// <summary>
    /// Reads the listing filters off the query string.
    /// </summary>
    /// <remarks>
    /// Bound by hand rather than through a model, because <c>attr.&lt;key&gt;</c> is an open set:
    /// a visitor filters on whatever attributes the category happens to carry, and no record type
    /// can name those in advance.
    /// </remarks>
    private static Result<ProductListQuery> ReadListQuery(HttpRequest request)
    {
        var query = request.Query;

        var brandIds = query["brandIds"]
            .Select(value => long.TryParse(value, CultureInfo.InvariantCulture, out var id) ? id : (long?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        var attributes = query
            .Where(pair => pair.Key.StartsWith(AttributePrefix, StringComparison.Ordinal))
            .ToDictionary(
                pair => pair.Key[AttributePrefix.Length..],
                pair => (IReadOnlyList<string>)[.. pair.Value.Where(value => value is not null)!],
                StringComparer.Ordinal);

        var min = ReadMinor(query["priceMin"], "priceMin");
        if (min.IsFailure) return Result.Failure<ProductListQuery>(min.Error!);

        var max = ReadMinor(query["priceMax"], "priceMax");
        if (max.IsFailure) return Result.Failure<ProductListQuery>(max.Error!);

        var sort = ReadSort(query["sort"]);
        if (sort.IsFailure) return Result.Failure<ProductListQuery>(sort.Error!);

        return Result.Success(new ProductListQuery(
            query["currency"].FirstOrDefault() ?? string.Empty,
            long.TryParse(query["categoryId"].FirstOrDefault(), CultureInfo.InvariantCulture, out var categoryId)
                ? categoryId : null,
            brandIds.Count > 0 ? brandIds : null,
            attributes.Count > 0 ? attributes : null,
            min.Value,
            max.Value,
            query["q"].FirstOrDefault(),
            sort.Value,
            query["cursor"].FirstOrDefault(),
            int.TryParse(query["limit"].FirstOrDefault(), CultureInfo.InvariantCulture, out var limit)
                ? limit : null));
    }

    /// <summary>
    /// A price bound is a two-decimal string like every other amount on the wire. <c>10</c> is
    /// refused rather than read as ten: the one thing worse than rejecting an amount is guessing
    /// which one was meant.
    /// </summary>
    private static Result<long?> ReadMinor(string? text, string name)
    {
        if (string.IsNullOrEmpty(text)) return Result.Success<long?>(null);

        return MoneyText.TryParseMinor(text, out var minor)
            ? Result.Success<long?>(minor)
            : Result.Failure<long?>(new Error(
                "VALIDATION_FAILED",
                $"'{text}' is not a {name}. Use exactly two decimal places, for example \"10.00\".",
                400));
    }

    private static Result<StorefrontSort?> ReadSort(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Result.Success<StorefrontSort?>(null);

        return Enum.TryParse<StorefrontSort>(text, ignoreCase: true, out var sort)
            ? Result.Success<StorefrontSort?>(sort)
            : Result.Failure<StorefrontSort?>(new Error(
                "VALIDATION_FAILED", $"'{text}' is not a sort order.", 400));
    }
}
