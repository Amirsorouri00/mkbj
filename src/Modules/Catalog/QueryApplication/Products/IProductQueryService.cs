using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Catalog.Contracts.Products;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Products;

public interface IProductQueryService
{
    Task<Result<ProductSnapshot>> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<ProductSnapshot>> ListAsync(int page, int pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<ProductAdminRow>> GetProductRowsAsync(string? search, bool includeInactive, int page, int pageSize, CancellationToken ct = default);
}
