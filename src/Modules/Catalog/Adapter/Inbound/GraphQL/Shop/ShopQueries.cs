using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Modules.Catalog.QueryApplication.Storefront;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Shop;

/// <summary>One listing filter, as a GraphQL input.</summary>
/// <remarks>
/// Attributes are a list of key/value pairs rather than a map: GraphQL has no open-ended object
/// type, and the set of attribute keys is whatever the category carries.
/// </remarks>
public sealed record ShopProductFilterInput(
    long? CategoryId,
    IReadOnlyList<long>? BrandIds,
    IReadOnlyList<AttributeFilterInput>? Attributes,
    string? PriceMin,
    string? PriceMax,
    string? Q);

public sealed record AttributeFilterInput(string Key, IReadOnlyList<string> Values);

/// <summary>
/// The storefront over GraphQL — the same queries the REST shop serves, for a client that would
/// rather ask for a page in one round trip.
/// </summary>
/// <remarks>
/// <para>
/// Every field is <c>shop</c>-prefixed: this extends the one shared <c>Query</c> type, where the
/// admin surface already owns <c>categories</c> and <c>products</c>.
/// </para>
/// <para>
/// <c>[AllowAnonymous]</c> sits on each resolver. It could not sit on the class even by mistake —
/// HotChocolate targets it at methods and properties only, so the compiler refuses — but
/// <c>[Authorize]</c> can, and a type-level one on an <c>[ExtendObjectType]</c> is merged onto the
/// whole <c>Query</c>, gating every other module&#39;s fields. <c>GraphQLGateScopeTests</c> fails
/// the build on that, which is why this file keeps the same per-resolver habit.
/// </para>
/// </remarks>
[ExtendObjectType(typeof(Query))]
public sealed class ShopQueries
{
    [AllowAnonymous]
    public async Task<HomeDto> ShopHomeAsync(
        string currency, [Service] IStorefrontQueries queries, CancellationToken ct = default) =>
        (await queries.GetHomeAsync(currency, ct)).ValueOrThrow();

    [AllowAnonymous]
    public Task<IReadOnlyList<CategoryTreeNodeDto>> ShopCategoriesAsync(
        [Service] IStorefrontQueries queries, CancellationToken ct = default) =>
        queries.GetCategoryTreeAsync(ct);

    [AllowAnonymous]
    public async Task<ProductPageDto> ShopProductsAsync(
        string currency,
        [Service] IStorefrontQueries queries,
        ShopProductFilterInput? filter = null,
        StorefrontSort? sort = null,
        string? after = null,
        int? first = null,
        CancellationToken ct = default)
    {
        var query = ShopFilterReader.Read(currency, filter, sort, after, first);

        return query.IsFailure
            ? throw ShopFilterReader.AsGraphQLError(query.Error!)
            : (await queries.GetProductsAsync(query.Value!, ct)).ValueOrThrow();
    }

    [AllowAnonymous]
    public async Task<FacetSetDto> ShopFacetsAsync(
        string currency, [Service] IStorefrontQueries queries,
        long? categoryId = null, CancellationToken ct = default) =>
        (await queries.GetFacetsAsync(categoryId, currency, ct)).ValueOrThrow();

    [AllowAnonymous]
    public async Task<ProductDetailDto> ShopProductAsync(
        string slug, string currency,
        [Service] IStorefrontQueries queries, CancellationToken ct = default) =>
        (await queries.GetProductBySlugAsync(slug, currency, ct)).ValueOrThrow();

    [AllowAnonymous]
    public async Task<ProductPageDto> ShopSearchAsync(
        string q, string currency, [Service] IStorefrontQueries queries,
        string? after = null, int? first = null, CancellationToken ct = default) =>
        (await queries.SearchAsync(q, currency, after, first, ct)).ValueOrThrow();
}
