using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;

/// <summary>
/// What a set of variants costs in one currency, right now.
/// </summary>
public interface IPriceQuotePort
{
    /// <summary>
    /// Quotes the variants that can be quoted, keyed by variant id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A variant with no price, no usable rate, or a converted amount below one minor unit is
    /// <b>omitted</b> rather than reported. The caller is composing a basket or rendering a
    /// listing, and in both the right move is to carry on without it — a per-item error would
    /// make every caller write the same loop to discard them.
    /// </para>
    /// <para>
    /// A currency the seller has not enabled fails the <b>whole call</b>, because that is not a
    /// fact about any variant: it is the caller asking for something the shop does not sell in.
    /// </para>
    /// </remarks>
    Task<Result<IReadOnlyDictionary<long, PriceQuote>>> QuoteAsync(
        IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>
    /// The pricing version these quotes were computed against, so a cart holding an older number
    /// knows its prices may have moved.
    /// </summary>
    Task<long> GetVersionAsync(CancellationToken ct = default);
}
