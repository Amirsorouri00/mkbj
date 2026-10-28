using EwaysVNext.Domain.Common;
using FluentValidation;
using Microsoft.Extensions.Logging;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Validation;
using EwaysVNext.Modules.Catalog.Contracts.Products;
using EwaysVNext.Domain.ValueObjects;

namespace EwaysVNext.Modules.Catalog.Application.Products.Services;

public sealed class ProductService : IProductService, IProductAvailabilityContract
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProductService> _logger;

    private readonly CreateProductValidator _createProductValidator;
    private readonly ChangePriceValidator _changePriceValidator;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;

    public ProductService(
        IProductRepository products,
        IUnitOfWork unitOfWork,
        ILogger<ProductService> logger,
        CreateProductValidator createProductValidator,
        ChangePriceValidator changePriceValidator,
        IClock clock,
        IIdGenerator idGenerator)
    {
        _products = products;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _createProductValidator = createProductValidator;
        _changePriceValidator = changePriceValidator;
        _clock = clock;
        _idGenerator = idGenerator;
    }

    public async Task<bool> ExistsAndIsActiveAsync(long productId, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(productId, ct);
        return product?.IsActive == true;
    }

    public async Task<Result<ProductSnapshot>> GetProductAsync(long productId, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure<ProductSnapshot>(new Error("PRODUCT_NOT_FOUND", "Product was not found."));
        return Result.Success(new ProductSnapshot(product.Id, product.Name, product.Price.Amount, product.Price.Currency));
    }

    public async Task<Result<ProductSnapshot>> CreateProductAsync(
        string sku, string name, decimal price, string currency, string? description, CancellationToken ct = default)
    {
        var validationResult = await _createProductValidator.ValidateAsync((sku, name, price, currency, description), ct);
        if (!validationResult.IsValid)
            return Result.Failure<ProductSnapshot>(new Error("VALIDATION_FAILED", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))));

        var skuVo = Sku.Create(sku);
        if (await _products.SkuExistsAsync(skuVo.Value, ct))
            return Result.Failure<ProductSnapshot>(new Error("SKU_TAKEN", $"SKU '{skuVo.Value}' already exists."));

        var priceVo = Money.Create(price, currency);
        var product = Product.Create(_idGenerator.Generate(), skuVo, name, priceVo, _clock.UtcNow, description);

        await _products.AddAsync(product, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ProductSnapshot(product.Id, product.Name, product.Price.Amount, product.Price.Currency));
    }

    public async Task<Result> ChangePriceAsync(long productId, decimal newPrice, string currency, CancellationToken ct = default)
    {
        var validationResult = await _changePriceValidator.ValidateAsync((productId, newPrice, currency), ct);
        if (!validationResult.IsValid)
            return Result.Failure(new Error("VALIDATION_FAILED", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))));

        var product = await _products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(new Error("PRODUCT_NOT_FOUND", "Product was not found."));

        product.ChangePrice(Money.Create(newPrice, currency), _clock.UtcNow);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeactivateProductAsync(long productId, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(new Error("PRODUCT_NOT_FOUND", "Product was not found."));
        try { product.Deactivate(_clock.UtcNow); }
        catch (DomainException dex) { return Result.Failure(new Error(dex.Code, dex.Message)); }
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
