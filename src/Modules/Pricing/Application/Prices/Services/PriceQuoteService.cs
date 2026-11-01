using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Services;
using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application.Prices.Services;

public sealed class PriceQuoteService(
    IVariantPriceRepository prices,
    FxBookLoader books,
    IPricingChangeNotifier notifier) : IPriceQuotePort
{
    public async Task<Result<IReadOnlyDictionary<long, PriceQuote>>> QuoteAsync(
        IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(variantIds);

        var (book, currencies) = await books.LoadAsync(ct);

        var enabled = FxBookLoader.RequireEnabled(currencies, currency);
        if (enabled.IsFailure)
            return Result.Failure<IReadOnlyDictionary<long, PriceQuote>>(enabled.Error!);

        var quotes = new Dictionary<long, PriceQuote>(variantIds.Count);
        if (variantIds.Count == 0)
            return Result.Success<IReadOnlyDictionary<long, PriceQuote>>(quotes);

        // The book is resolved from, not re-read: quoting a page of variants asks the same few
        // pairs over and over, and each answer has to be the same one.
        foreach (var price in await prices.GetByVariantIdsAsync(variantIds, ct))
        {
            var quote = PriceResolver.Resolve(
                price,
                enabled.Value!,
                (from, to) => FxRateResolver.Resolve(book, from, to, now),
                out _);

            // Unquotable variants are dropped, not reported. See IPriceQuotePort.
            if (quote is not null) quotes[price.VariantId] = quote;
        }

        return Result.Success<IReadOnlyDictionary<long, PriceQuote>>(quotes);
    }

    public Task<long> GetVersionAsync(CancellationToken ct = default) =>
        notifier.CurrentVersionAsync(ct);
}
