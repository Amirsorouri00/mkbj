namespace EwaysVNext.Modules.Catalog.Contracts.Products;

public interface IProductAvailabilityContract
{
    Task<bool> ExistsAndIsActiveAsync(long productId, CancellationToken ct = default);
}
