using EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Services;

/// <summary>
/// Recomputes the resolved-rate table from the current book.
/// </summary>
public sealed class ResolvedRateProjectionService(
    ICurrencyRepository currencies,
    IFxRateRepository rates,
    IFxRateOverrideRepository overrides,
    IResolvedRateProjection projection,
    IPricingPolicyGateway policy)
{
    public async Task RebuildAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var all = await currencies.GetAllAsync(ct);
        var book = new FxRateBook(
            await rates.GetLatestPerPairAsync(ct),
            await overrides.GetActiveAsync(ct),
            await policy.GetFxMaxRateAgeAsync(ct));

        var rows = new List<ResolvedRateRow>();

        foreach (var from in all)
        {
            // Every currency is a possible base — a price may have been collected in a currency
            // the seller no longer offers — but only enabled ones are worth quoting into.
            foreach (var to in all.Where(candidate => candidate.IsEnabled))
            {
                if (string.Equals(from.Code, to.Code, StringComparison.Ordinal)) continue;

                var resolved = FxRateResolver.Resolve(book, from.Code, to.Code, now);

                // A pair with no usable rate is left out rather than written as null. The
                // storefront joins this table, so an absent row means "cannot be priced in that
                // currency", which is exactly what it means.
                if (resolved.IsFailure || resolved.Value!.Snapshot is null) continue;

                var snapshot = resolved.Value.Snapshot;
                rows.Add(new ResolvedRateRow(
                    from.Code,
                    to.Code,
                    resolved.Value.Rate,
                    snapshot.Provenance,
                    snapshot.FxRateId,
                    snapshot.FxOverrideId,
                    snapshot.RateAt,
                    resolved.Value.ValidUntil));
            }
        }

        await projection.ReplaceAllAsync(rows, ct);
    }
}
