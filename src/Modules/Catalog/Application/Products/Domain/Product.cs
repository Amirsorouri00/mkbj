using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

public sealed class Product : AggregateRoot<long>
{
    public Sku Sku { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Money Price { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private Product() { }

    public static Product Create(long id, Sku sku, string name, Money price, DateTimeOffset now,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("PRODUCT_NAME_EMPTY", ProductMessages.NameEmpty);
        if (name.Length > 200) throw new DomainException("PRODUCT_NAME_TOO_LONG", ProductMessages.NameTooLong);

        var product = new Product
        {
            Id = id,
            Sku = sku,
            Name = name.Trim(),
            Description = description?.Trim(),
            Price = price,
            IsActive = true,
            CreatedAt = now
        };

        product.Raise(new ProductCreatedDomainEvent(product.Id, product.Name, product.Price.Amount));

        return product;
    }

    public void ChangePrice(Money newPrice, DateTimeOffset now)
    {
        if (!IsActive) throw new DomainException("PRODUCT_INACTIVE", ProductMessages.InactiveCannotBeModified);
        if (newPrice.Amount == Price.Amount) return;
        Price = newPrice;
        UpdatedAt = now;
        Raise(new ProductPriceChangedDomainEvent(Id, newPrice.Amount));
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive) return;
        IsActive = false;
        UpdatedAt = now;
        Raise(new ProductDeactivatedDomainEvent(Id));
    }
}
