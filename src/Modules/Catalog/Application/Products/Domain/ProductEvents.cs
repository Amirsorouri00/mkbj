using EwaysVNext.Domain.Events;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

public sealed record ProductActivatedDomainEvent(long ProductId) : DomainEvent;

/// <param name="ProductId">The product that was switched off.</param>
/// <param name="ByAdmin">
/// Carried on the event because a handler acting on it — the storefront refresh, an audit entry —
/// needs to know whether this was a decision or an import's arithmetic.
/// </param>
public sealed record ProductDeactivatedDomainEvent(long ProductId, bool ByAdmin) : DomainEvent;

public sealed record VariantActivationChangedDomainEvent(long ProductId, long VariantId, bool IsActive) : DomainEvent;
