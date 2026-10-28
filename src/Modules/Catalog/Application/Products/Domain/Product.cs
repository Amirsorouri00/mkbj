using System.Globalization;
using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

public sealed class Product : AggregateRoot<long>
{
    public Sku Sku { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Money Price { get; private set; } = null!;
    public int Stock { get; private set; }
    public int ReservedStock { get; private set; }
    public int AvailableStock => Stock - ReservedStock;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private readonly List<ProductReservation> _reservations = new();
    public IReadOnlyCollection<ProductReservation> Reservations => _reservations.AsReadOnly();

    private Product() { }

    public static Product Create(long id, Sku sku, string name, Money price, DateTimeOffset now,
        int initialStock = 0, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("PRODUCT_NAME_EMPTY", ProductMessages.NameEmpty);
        if (name.Length > 200) throw new DomainException("PRODUCT_NAME_TOO_LONG", ProductMessages.NameTooLong);
        if (initialStock < 0) throw new DomainException("PRODUCT_STOCK_NEGATIVE", ProductMessages.StockNegative);

        var product = new Product
        {
            Id = id,
            Sku = sku,
            Name = name.Trim(),
            Description = description?.Trim(),
            Price = price,
            Stock = initialStock,
            IsActive = true,
            CreatedAt = now
        };

        product.Raise(new ProductCreatedDomainEvent(product.Id, product.Name, product.Price.Amount));
        if (initialStock > 0)
            product.Raise(new ProductStockAdjustedDomainEvent(product.Id, initialStock));

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

    public void AdjustStock(int delta, DateTimeOffset now)
    {
        if (!IsActive) throw new DomainException("PRODUCT_INACTIVE", ProductMessages.InactiveCannotBeModified);
        var newStock = Stock + delta;
        if (newStock < 0) throw new DomainException("PRODUCT_STOCK_NEGATIVE", ProductMessages.ResultingStockNegative);
        Stock = newStock;
        UpdatedAt = now;
        Raise(new ProductStockAdjustedDomainEvent(Id, newStock));
    }

    public ProductReservation ReserveStock(int quantity, long reservationId, TimeSpan ttl, DateTimeOffset now)
    {
        if (!IsActive) throw new DomainException("PRODUCT_INACTIVE", ProductMessages.InactiveCannotBeReserved);
        if (quantity <= 0) throw new DomainException("RESERVATION_QTY", ProductMessages.ReservationQuantity);
        if (quantity > AvailableStock)
            throw new DomainException("STOCK_INSUFFICIENT", string.Format(CultureInfo.InvariantCulture, ProductMessages.StockInsufficientComposite, AvailableStock));

        var reservation = new ProductReservation(reservationId, Id, quantity, now, now + ttl);
        _reservations.Add(reservation);
        ReservedStock += quantity;
        UpdatedAt = now;
        Raise(new ProductReservedDomainEvent(Id, quantity, reservationId));
        return reservation;
    }

    public void ReleaseReservation(long reservationId, DateTimeOffset now)
    {
        var r = _reservations.FirstOrDefault(x => x.Id == reservationId);
        if (r is null) return;

        if (r.IsCommitted)
            throw new DomainException("RESERVATION_ALREADY_COMMITTED", ProductMessages.ReservationAlreadyCommitted);

        _reservations.Remove(r);
        ReservedStock -= r.Quantity;
        UpdatedAt = now;
        Raise(new ProductReservationReleasedDomainEvent(Id, r.Quantity, reservationId));
    }

    public void CommitReservation(long reservationId, DateTimeOffset now)
    {
        var r = _reservations.FirstOrDefault(x => x.Id == reservationId);
        if (r is null) throw new DomainException("RESERVATION_NOT_FOUND", ProductMessages.ReservationNotFound);
        if (r.IsCommitted) return;

        r.Commit(now);
        ReservedStock -= r.Quantity;
        Stock -= r.Quantity;
        UpdatedAt = now;
        Raise(new ProductStockAdjustedDomainEvent(Id, Stock));
    }

    public void RevertCommitment(long reservationId, DateTimeOffset now)
    {
        var r = _reservations.FirstOrDefault(x => x.Id == reservationId && x.IsCommitted)
                ?? throw new DomainException("RESERVATION_NOT_COMMITTED", ProductMessages.ReservationNotCommitted);

        r.Revert();
        ReservedStock += r.Quantity;
        Stock += r.Quantity;
        UpdatedAt = now;
        Raise(new ProductStockAdjustedDomainEvent(Id, Stock));
    }

    public IReadOnlyList<long> ReleaseExpiredReservations(DateTimeOffset now)
    {
        List<long> released = [];
        foreach (var r in _reservations.Where(x => !x.IsCommitted && x.ExpiresAt <= now).ToList())
        {
            _reservations.Remove(r);
            ReservedStock -= r.Quantity;
            released.Add(r.Id);
            Raise(new ProductReservationReleasedDomainEvent(Id, r.Quantity, r.Id));
        }
        if (released.Count > 0) UpdatedAt = now;
        return released;
    }

    public IReadOnlyList<long> PruneCommittedReservations(DateTimeOffset now, TimeSpan retention)
    {
        if (retention < TimeSpan.Zero)
            throw new DomainException("RETENTION_NEGATIVE", ProductMessages.RetentionNegative);

        var pruneBefore = now - retention;
        List<long> pruned = [];
        foreach (var r in _reservations.Where(x => x.IsCommitted && x.CommittedAt <= pruneBefore).ToList())
        {
            _reservations.Remove(r);
            pruned.Add(r.Id);
        }
        return pruned;
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive) return;
        IsActive = false;
        UpdatedAt = now;
        Raise(new ProductDeactivatedDomainEvent(Id));
    }
}
