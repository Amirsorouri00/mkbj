using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

public interface ICatalogAdminService
{
    Task<Result<long>> CreateProductAsync(string sku, string name, decimal price, string currency, int initialStock, string? description, CancellationToken ct = default);
    Task<Result> ChangePriceAsync(long productId, decimal newPrice, string currency, CancellationToken ct = default);
    Task<Result> AdjustStockAsync(long productId, int delta, CancellationToken ct = default);
    Task<Result> DeactivateProductAsync(long productId, CancellationToken ct = default);
}
