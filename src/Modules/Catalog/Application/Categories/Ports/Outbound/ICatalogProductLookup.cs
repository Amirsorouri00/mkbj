namespace EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;

public interface ICatalogProductLookup
{
    Task<bool> ExistsAndIsActiveAsync(long productId, CancellationToken ct = default);
}
