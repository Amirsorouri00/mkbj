using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;

/// <summary>
/// How an import sets a price. Idempotent by construction, like the catalogue's publishing port:
/// the same observation arriving again overwrites the same row rather than adding another.
/// </summary>
public interface IPricePublishingPort
{
    Task<Result> SetReferencePriceAsync(
        long variantId, long amountMinor, string currency, PriceProvenance provenance,
        CancellationToken ct = default);
}
