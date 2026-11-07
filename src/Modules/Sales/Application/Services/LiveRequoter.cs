using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Sales.Application.Services;

/// <summary>What a re-quote decided about one composed basket.</summary>
/// <remarks>
/// <c>DroppedVariantIds</c> empty means the basket survived and <c>Lines</c> can be sold.
/// Otherwise the basket is void and those variants must be kept out of the next attempt.
/// </remarks>
internal sealed record RequoteResult(
    IReadOnlyList<OrderLineInput> Lines, IReadOnlyList<long> DroppedVariantIds);

/// <summary>
/// Re-checks a composed basket against live catalogue and prices before anything is sold.
/// </summary>
/// <remarks>
/// <para>
/// The composition runs against a pool that may be up to thirty seconds old. That is fine for
/// <i>choosing</i> a basket and not fine for <i>selling</i> one: in those seconds a variant can
/// be withdrawn or a price can move, and the partner would be billed for something the shop no
/// longer offers at a price it no longer honours.
/// </para>
/// <para>
/// So the snapshots stored on the order come from <b>this</b> read, never from the cached pool.
/// A line's recorded reference price is the live one even when both bands would have allowed the
/// chosen price — the order must say what the catalogue actually said at the moment of sale.
/// </para>
/// </remarks>
internal sealed class LiveRequoter(ISalesCatalogGateway catalog, ISalesPricingGateway pricing)
{
    public async Task<RequoteResult> ResolveAsync(
        IReadOnlyList<ComposedLine> lines,
        string currency,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var chosenIds = lines.Select(line => line.VariantId).ToArray();

        var variants = (await catalog.GetSellableAsync(chosenIds, ct))
            .ToDictionary(variant => variant.VariantId);

        var quoted = await pricing.QuoteAsync(chosenIds, currency, now, ct);

        // A quote failure here is not a dropped line but a dead basket: the currency stopped
        // being sellable between composing and selling.
        var quotes = quoted.IsSuccess
            ? quoted.Value!.ToDictionary(quote => quote.VariantId)
            : [];

        var resolved = new List<OrderLineInput>(lines.Count);
        var dropped = new List<long>();

        foreach (var line in lines)
        {
            if (!variants.TryGetValue(line.VariantId, out var variant)
                || !quotes.TryGetValue(line.VariantId, out var quote)
                || line.UnitPriceMinor < quote.LoMinor
                || line.UnitPriceMinor > quote.HiMinor)
            {
                dropped.Add(line.VariantId);
                continue;
            }

            resolved.Add(new OrderLineInput(
                LineId: 0,
                new VariantSnapshot(
                    variant.VariantId,
                    variant.ProductId,
                    variant.Sku,
                    variant.ProductTitle,
                    variant.BrandName,
                    variant.CategoryPath,
                    variant.Attributes),
                line.Quantity,
                line.UnitPriceMinor,
                quote.Snapshot));
        }

        return new RequoteResult(dropped.Count == 0 ? resolved : [], dropped);
    }
}
