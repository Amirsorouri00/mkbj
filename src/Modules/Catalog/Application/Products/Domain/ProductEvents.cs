using EwaysVNext.Domain.Events;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

public sealed record ProductCreatedDomainEvent(long ProductId, string Name, decimal Price) : DomainEvent;
public sealed record ProductPriceChangedDomainEvent(long ProductId, decimal NewPrice) : DomainEvent;
public sealed record ProductStockAdjustedDomainEvent(long ProductId, int NewStock) : DomainEvent;
public sealed record ProductReservedDomainEvent(long ProductId, int Quantity, long ReservationId) : DomainEvent;
public sealed record ProductReservationReleasedDomainEvent(long ProductId, int Quantity, long ReservationId) : DomainEvent;
public sealed record ProductDeactivatedDomainEvent(long ProductId) : DomainEvent;
