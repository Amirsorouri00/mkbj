using System.Text.Json;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Search;

/// <summary>Which query a search import has reached, and how far into its results.</summary>
internal sealed record SearchCursor(int QueryIndex, int Offset);

/// <summary>
/// Turns search results into pages to read.
/// </summary>
/// <remarks>
/// <para>
/// Search decides <b>where to look</b>; the crawler decides what is there. A hit is handed to
/// the same fetch-and-extract path a crawl uses, so a page found by searching is subject to
/// exactly the same robots.txt, rights-policy and address checks. Importing a search snippet
/// directly would put somebody else's editorial summary into this catalogue as a product
/// description, and would skip every one of those checks.
/// </para>
/// <para>
/// The job's include and exclude domains are applied to the hits before anything is fetched: a
/// search for "steel kettle" returns the whole web, and an import is for this seller's chosen
/// suppliers.
/// </para>
/// </remarks>
internal sealed class WebSearchSourceAdapter(
    ISearchProvider search,
    ISourceAdapter[] crawlers,
    ILogger<WebSearchSourceAdapter> logger) : ISourceAdapter
{
    public bool Handles(ImportMode mode) => mode is ImportMode.WebSearch or ImportMode.Combined;

    public async Task<DiscoveryBatch> DiscoverAsync(
        ImportJobSpec spec, string? cursor, int batchSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!search.IsEnabled)
        {
            // Disabled rather than broken. A Combined job still has its crawl sources, and a
            // WebSearch job finds nothing and completes rather than failing in a way that looks
            // like the import is faulty.
            logger.LogInformation(
                "Job {JobId} asked for web search, which is not configured.", spec.JobId);

            return new DiscoveryBatch([], null, false);
        }

        var state = Restore(cursor);

        if (state.QueryIndex >= spec.Queries.Count)
            return new DiscoveryBatch([], null, false);

        var hits = await search.SearchAsync(spec.Queries[state.QueryIndex], state.Offset, ct);

        if (hits.Count == 0)
        {
            // This query is exhausted; move to the next one rather than stopping the job.
            var next = new SearchCursor(state.QueryIndex + 1, 0);

            return new DiscoveryBatch(
                [], Save(next), next.QueryIndex < spec.Queries.Count);
        }

        var urls = hits
            .Select(hit => hit.Url)
            .Where(url => Allowed(url, spec))
            .ToList();

        var listings = await ReadAsync(spec, urls, batchSize, ct);

        return new DiscoveryBatch(
            listings,
            Save(new SearchCursor(state.QueryIndex, state.Offset + hits.Count)),
            true);
    }

    /// <summary>
    /// Reads each hit through the crawl path, so every check still applies.
    /// </summary>
    /// <remarks>
    /// One page per source entry rather than a crawl from each: a search hit is a page worth
    /// reading, not an invitation to walk the site it is on.
    /// </remarks>
    private async Task<List<RawListing>> ReadAsync(
        ImportJobSpec spec, IReadOnlyList<string> urls, int batchSize, CancellationToken ct)
    {
        var crawler = crawlers.FirstOrDefault(adapter => adapter.Handles(ImportMode.Crawl));
        if (crawler is null) return [];

        var listings = new List<RawListing>();

        foreach (var url in urls.Take(batchSize))
        {
            ct.ThrowIfCancellationRequested();

            // Depth zero: fetch this page, follow nothing.
            var single = spec with
            {
                Sources = [url],
                Limits = spec.Limits with { MaxPages = 1, MaxDepth = 0 },
            };

            var batch = await crawler.DiscoverAsync(single, null, batchSize, ct);

            listings.AddRange(batch.Listings);
        }

        return listings;
    }

    private static bool Allowed(string url, ImportJobSpec spec)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var domain = SourceDomain.Normalize(uri.Host);

        if (spec.ExcludeDomains.Contains(domain, StringComparer.Ordinal)) return false;

        return spec.IncludeDomains.Count == 0
            || spec.IncludeDomains.Contains(domain, StringComparer.Ordinal);
    }

    private static SearchCursor Restore(string? cursor)
    {
        if (cursor is null) return new SearchCursor(0, 0);

        try
        {
            return JsonSerializer.Deserialize<SearchCursor>(cursor) ?? new SearchCursor(0, 0);
        }
        catch (JsonException)
        {
            return new SearchCursor(0, 0);
        }
    }

    private static string Save(SearchCursor cursor) => JsonSerializer.Serialize(cursor);
}
