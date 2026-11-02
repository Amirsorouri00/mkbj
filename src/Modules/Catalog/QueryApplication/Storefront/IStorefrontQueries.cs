using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Storefront;

/// <summary>
/// What a browsing visitor is shown. Read-only, and read from the materialized views.
/// </summary>
/// <remarks>
/// ADR-0020: nothing here may be consumed by a purchasing path. These answers are stale by
/// construction — the views are refreshed on a schedule — and a basket priced from a stale row
/// would not total what the buyer was quoted.
/// </remarks>
public interface IStorefrontQueries
{
    Task<Result<HomeDto>> GetHomeAsync(string currency, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryTreeNodeDto>> GetCategoryTreeAsync(CancellationToken ct = default);

    Task<Result<ProductDetailDto>> GetProductBySlugAsync(
        string slug, string currency, CancellationToken ct = default);
}
