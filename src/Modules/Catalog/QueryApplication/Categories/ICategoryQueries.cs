using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Categories;

/// <summary>Read-side queries for the public, effectively active category tree.</summary>
public interface ICategoryQueries
{
    Task<IReadOnlyList<CategoryTreeNodeDto>> GetTreeAsync(CancellationToken ct = default);
    Task<Result<CategoryDto>> GetByIdAsync(long categoryId, CancellationToken ct = default);
    Task<Result<CategoryProductPageDto>> GetProductIdsAsync(
        long categoryId, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryDto>> GetByProductIdAsync(long productId, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryTreeNodeDto>> GetActiveTreeAsync(CancellationToken ct = default) =>
        GetTreeAsync(ct);

    Task<Result<CategoryDto>> GetCategoryAsync(long categoryId, CancellationToken ct = default) =>
        GetByIdAsync(categoryId, ct);

    Task<Result<CategoryProductPageDto>> GetCategoryProductsAsync(
        long categoryId, int page, int pageSize, CancellationToken ct = default) =>
        GetProductIdsAsync(categoryId, page, pageSize, ct);

    Task<IReadOnlyList<CategoryDto>> GetProductCategoriesAsync(
        long productId, CancellationToken ct = default) =>
        GetByProductIdAsync(productId, ct);
}
