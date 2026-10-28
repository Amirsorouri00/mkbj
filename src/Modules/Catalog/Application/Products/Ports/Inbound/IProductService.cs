using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Catalog.Contracts.Products;

namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

public interface IProductService
{
    Task<bool> ExistsAndIsActiveAsync(long productId, CancellationToken ct = default);
    Task<Result<ProductSnapshot>> GetProductAsync(long productId, CancellationToken ct = default);
    Task<Result<ProductSnapshot>> CreateProductAsync(string sku, string name, decimal price, string currency, int initialStock, string? description, CancellationToken ct = default);
    Task<Result> ChangePriceAsync(long productId, decimal newPrice, string currency, CancellationToken ct = default);
    Task<Result> AdjustStockAsync(long productId, int delta, CancellationToken ct = default);
    Task<Result<ReservationSnapshot>> ReserveStockAsync(long productId, int quantity, TimeSpan ttl, CancellationToken ct = default);
    Task<Result> CommitReservationAsync(long productId, long reservationId, CancellationToken ct = default);
    Task<Result> ReleaseReservationAsync(long productId, long reservationId, CancellationToken ct = default);
    Task<Result> DeactivateProductAsync(long productId, CancellationToken ct = default);
    Task<Result> RevertCommitmentAsync(long productId, long reservationId, CancellationToken ct = default);
}
