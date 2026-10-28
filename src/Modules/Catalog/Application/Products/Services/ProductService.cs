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
    private static readonly TimeSpan DefaultReservationTtl = TimeSpan.FromMinutes(10);

    private readonly IProductRepository _products;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProductService> _logger;

    private readonly CreateProductValidator _createProductValidator;
    private readonly ChangePriceValidator _changePriceValidator;
    private readonly ReserveStockValidator _reserveStockValidator;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;

    public ProductService(
        IProductRepository products,
        IUnitOfWork unitOfWork,
        ILogger<ProductService> logger,
        CreateProductValidator createProductValidator,
        ChangePriceValidator changePriceValidator,
        ReserveStockValidator reserveStockValidator,
        IClock clock,
        IIdGenerator idGenerator)
    {
        _products = products;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _createProductValidator = createProductValidator;
        _changePriceValidator = changePriceValidator;
        _reserveStockValidator = reserveStockValidator;
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
        return Result.Success(new ProductSnapshot(product.Id, product.Name, product.Price.Amount, product.Price.Currency, product.AvailableStock));
    }

    public async Task<Result<ProductSnapshot>> CreateProductAsync(
        string sku, string name, decimal price, string currency, int initialStock, string? description, CancellationToken ct = default)
    {
        var validationResult = await _createProductValidator.ValidateAsync((sku, name, price, currency, initialStock, description), ct);
        if (!validationResult.IsValid)
            return Result.Failure<ProductSnapshot>(new Error("VALIDATION", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))));

        var skuVo = Sku.Create(sku);
        if (await _products.SkuExistsAsync(skuVo.Value, ct))
            return Result.Failure<ProductSnapshot>(new Error("SKU_TAKEN", $"SKU '{skuVo.Value}' already exists."));

        var priceVo = Money.Create(price, currency);
        var product = Product.Create(_idGenerator.Generate(), skuVo, name, priceVo, _clock.UtcNow, initialStock, description);

        await _products.AddAsync(product, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ProductSnapshot(product.Id, product.Name, product.Price.Amount, product.Price.Currency, product.AvailableStock));
    }

    public async Task<Result> ChangePriceAsync(long productId, decimal newPrice, string currency, CancellationToken ct = default)
    {
        var validationResult = await _changePriceValidator.ValidateAsync((productId, newPrice, currency), ct);
        if (!validationResult.IsValid)
            return Result.Failure(new Error("VALIDATION", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))));

        var product = await _products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(new Error("PRODUCT_NOT_FOUND", "Product was not found."));

        product.ChangePrice(Money.Create(newPrice, currency), _clock.UtcNow);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> AdjustStockAsync(long productId, int delta, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(new Error("PRODUCT_NOT_FOUND", "Product was not found."));

        product.AdjustStock(delta, _clock.UtcNow);
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

    public async Task<Result<ReservationSnapshot>> ReserveStockAsync(long productId, int quantity, TimeSpan ttl, CancellationToken ct = default)
    {
        var validationResult = await _reserveStockValidator.ValidateAsync((productId, quantity), ct);
        if (!validationResult.IsValid)
            return Result.Failure<ReservationSnapshot>(new Error("VALIDATION", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))));

        var product = await _products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure<ReservationSnapshot>(new Error("PRODUCT_NOT_FOUND", "Product was not found."));

        var reservationId = _idGenerator.Generate();
        try
        {
            product.ReserveStock(quantity, reservationId,
                ttl == TimeSpan.Zero ? DefaultReservationTtl : ttl, _clock.UtcNow);
        }
        catch (DomainException dex)
        {
            return Result.Failure<ReservationSnapshot>(new Error(dex.Code, dex.Message));
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new ReservationSnapshot(reservationId, product.Id, quantity, _clock.UtcNow + ttl));
    }

    public async Task<Result> CommitReservationAsync(long productId, long reservationId, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(new Error("PRODUCT_NOT_FOUND", "Product was not found."));

        try { product.CommitReservation(reservationId, _clock.UtcNow); }
        catch (DomainException dex) { return Result.Failure(new Error(dex.Code, dex.Message)); }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RevertCommitmentAsync(long productId, long reservationId, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(new Error("PRODUCT_NOT_FOUND", "Product was not found."));

        try { product.RevertCommitment(reservationId, _clock.UtcNow); }
        catch (DomainException dex) { return Result.Failure(new Error(dex.Code, dex.Message)); }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ReleaseReservationAsync(long productId, long reservationId, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(new Error("PRODUCT_NOT_FOUND", "Product was not found."));

        try { product.ReleaseReservation(reservationId, _clock.UtcNow); }
        catch (DomainException dex) { return Result.Failure(new Error(dex.Code, dex.Message)); }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
