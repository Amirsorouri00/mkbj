using EwaysVNext.Modules.Sales.Application;
using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound.Candidates;

/// <summary>
/// Builds the pool of variants a basket may be composed from, reusing it while it is still true.
/// </summary>
/// <remarks>
/// Both versions are read on every call. That is two cheap queries against two sequences, and it
/// buys the difference between "this pool is less than thirty seconds old" and "this pool still
/// describes the shop" — which is what stops a withdrawn product being sold.
/// </remarks>
internal sealed class CachedCandidatePoolGateway(
    ISalesCatalogGateway catalog,
    ISalesPricingGateway pricing,
    CandidatePoolCache cache,
    IOptions<SalesOptions> options) : ICandidatePoolGateway
{
    public async Task<Result<CandidatePool>> GetAsync(
        string currency, DateTimeOffset now, CancellationToken ct = default)
    {
        var ttl = TimeSpan.FromSeconds(options.Value.CandidateCacheSeconds);

        var catalogVersion = await catalog.GetVersionAsync(ct);
        var pricingVersion = await pricing.GetVersionAsync(ct);

        if (cache.Get(currency, catalogVersion, pricingVersion, now, ttl) is { } cached)
            return Result.Success(cached);

        var variants = await catalog.GetSellableAsync(null, ct);

        var quoted = await pricing.QuoteAsync(
            [.. variants.Select(variant => variant.VariantId)], currency, now, ct);

        // A currency the shop does not sell in is the caller's answer, not a cacheable pool.
        if (quoted.IsFailure) return Result.Failure<CandidatePool>(quoted.Error!);

        var byVariant = variants.ToDictionary(variant => variant.VariantId);

        // A variant with no quote is not a candidate. The quote port omits what it cannot price
        // — no price, no usable rate, an amount below one minor unit — and composing with it
        // would mean inventing a price for something the shop cannot price.
        var candidates = quoted.Value!
            .Where(quote => byVariant.ContainsKey(quote.VariantId))
            .Select(quote => new CompositionCandidate(
                quote.VariantId,
                byVariant[quote.VariantId].PrimaryCategoryId,
                quote.ReferenceMinor,
                quote.LoMinor,
                quote.HiMinor))
            .ToList();

        var pool = new CandidatePool(currency, catalogVersion, pricingVersion, candidates);
        cache.Put(currency, catalogVersion, pricingVersion, now, pool);

        return Result.Success(pool);
    }

    public void Invalidate(string currency) => cache.Invalidate(currency);
}
