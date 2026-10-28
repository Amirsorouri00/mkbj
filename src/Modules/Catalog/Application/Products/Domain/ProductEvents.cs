using EwaysVNext.Domain.Events;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

public sealed record ProductCreatedDomainEvent(long ProductId, string Name, decimal Price) : DomainEvent;
public sealed record ProductPriceChangedDomainEvent(long ProductId, decimal NewPrice) : DomainEvent;
public sealed record ProductDeactivatedDomainEvent(long ProductId) : DomainEvent;
