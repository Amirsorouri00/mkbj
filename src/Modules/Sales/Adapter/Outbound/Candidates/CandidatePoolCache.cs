using System.Collections.Concurrent;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound.Candidates;

/// <summary>
/// The most recent candidate pool per currency, held across requests.
/// </summary>
/// <remarks>
/// <para>
/// A singleton with a short TTL. Building a pool means reading every sellable variant and
/// quoting all of them, which is the expensive part of composing an order — and a burst of
/// partner orders in the same currency would otherwise do it once each.
/// </para>
/// <para>
/// The entry carries the catalogue and pricing versions it was built from, so staleness is
/// decided by what changed rather than only by how long ago it was. A TTL alone would keep
/// selling a withdrawn product for up to thirty seconds.
/// </para>
/// </remarks>
internal sealed class CandidatePoolCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed record Entry(
        long CatalogVersion, long PricingVersion, DateTimeOffset LoadedAt, CandidatePool Pool);

    /// <summary>
    /// The cached pool, if it was built from these versions and is still inside its TTL.
    /// </summary>
    public CandidatePool? Get(
        string currency, long catalogVersion, long pricingVersion, DateTimeOffset now, TimeSpan ttl)
    {
        if (!_entries.TryGetValue(currency, out var entry)) return null;

        if (entry.CatalogVersion != catalogVersion || entry.PricingVersion != pricingVersion)
            return null;

        // The TTL boundary is exclusive: at exactly the limit the entry has expired. An
        // inclusive boundary makes "a 30 second cache" mean 30 seconds plus one request.
        return now - entry.LoadedAt < ttl ? entry.Pool : null;
    }

    public void Put(
        string currency, long catalogVersion, long pricingVersion, DateTimeOffset now,
        CandidatePool pool) =>
        _entries[currency] = new Entry(catalogVersion, pricingVersion, now, pool);

    public void Invalidate(string currency) => _entries.TryRemove(currency, out _);
}
