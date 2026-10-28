using ProductEntity = EwaysVNext.Modules.Catalog.Application.Products.Domain.Product;

namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;

public interface IProductRepository
{
    Task<ProductEntity?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<ProductEntity?> GetBySkuAsync(string sku, CancellationToken ct = default);
    Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default);
    Task<IReadOnlyList<ProductEntity>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<ProductEntity>> FindWithReclaimableReservationsAsync(
        DateTimeOffset utcNow, TimeSpan committedRetention, CancellationToken ct = default);
    Task AddAsync(ProductEntity aggregate, CancellationToken ct = default);
}
