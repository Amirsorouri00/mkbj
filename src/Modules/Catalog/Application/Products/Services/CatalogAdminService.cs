using EwaysVNext.Domain.Common;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;

namespace EwaysVNext.Modules.Catalog.Application.Products.Services;

public sealed class CatalogAdminService(
    IProductService products,
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    IClock clock) : ICatalogAdminService
{
    public async Task<Result<long>> CreateProductAsync(string sku, string name, decimal price, string currency,
        string? description, CancellationToken ct = default)
    {
        var result = await products.CreateProductAsync(sku, name, price, currency, description, ct);
        return result.IsSuccess ? Result.Success(result.Value!.Id.Value) : Result.Failure<long>(result.Error!);
    }

    public Task<Result> ChangePriceAsync(long productId, decimal newPrice, string currency, CancellationToken ct = default)
        => products.ChangePriceAsync(productId, newPrice, currency, ct);

    public async Task<Result> DeactivateProductAsync(long productId, CancellationToken ct = default)
    {
        var product = await productRepository.GetByIdAsync(productId, ct);
        if (product is null)
            return Result.Failure(new Error("PRODUCT_NOT_FOUND", "Product was not found."));

        try { product.Deactivate(clock.UtcNow); }
        catch (DomainException dex) { return Result.Failure(new Error(dex.Code, dex.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
