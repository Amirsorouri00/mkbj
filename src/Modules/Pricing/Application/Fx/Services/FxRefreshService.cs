using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Services;

public sealed class FxRefreshService(
    IFxRateProvider provider,
    IFxRateRepository rates,
    IPricingChangeNotifier notifier,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : IFxRefreshService
{
    private const string Hub = "EUR";

    public async Task<Result<int>> RefreshAsync(CancellationToken ct = default)
    {
        var batch = await provider.FetchAsync(ct);

        // Nothing is written and nothing is announced: the rates we already hold are still the
        // best we have, and re-projecting on a failed fetch would only move their staleness
        // forward in the table without moving it in fact.
        return batch.IsFailure
            ? Result.Failure<int>(batch.Error!)
            : await IngestAsync(batch.Value!, ct);
    }

    public async Task<Result<int>> IngestAsync(FxProviderBatch batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        // Asked up front rather than relying on the unique index to reject duplicates: a refresh
        // that runs twice in a day is normal, and letting the save fail would discard the quotes
        // that were new alongside the ones that were not.
        var existing = await rates.GetExistingQuotesAsync(Hub, batch.Provider, batch.RateDate, ct);

        var added = new List<FxRate>();
        foreach (var quote in batch.Rates ?? [])
        {
            if (existing.Contains(quote.Quote.Trim().ToUpperInvariant())) continue;

            try
            {
                added.Add(FxRate.Record(
                    ids.Generate(), Hub, quote.Quote, quote.Rate, batch.Provider,
                    batch.RateDate, clock.UtcNow));
            }
            catch (DomainException)
            {
                // A currency the catalogue does not use, or a rate the provider published
                // malformed. One bad line must not cost us the rest of the day's rates.
            }
        }

        if (added.Count > 0)
        {
            await rates.AddRangeAsync(added, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        // Always, even when nothing was inserted. The projection is what decides which rates are
        // still fresh enough to quote from, and rates go stale by the clock moving rather than
        // by anything being written.
        await notifier.FxChangedAsync(ct);

        return Result.Success(added.Count);
    }
}
