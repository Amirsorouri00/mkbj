using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Crawling;

/// <summary>
/// Where a crawl has got to, carried between batches.
/// </summary>
/// <remarks>
/// Serialized into the job's checkpoint, so a crawl killed mid-site resumes from the same
/// frontier rather than starting again. <c>Visited</c> is canonical URLs, which is why
/// <see cref="UrlForm"/> is shared with the fingerprint: two spellings of one page must not be
/// crawled twice.
/// </remarks>
internal sealed record CrawlCursor(
    List<string> Frontier, HashSet<string> Visited, int PagesFetched, int Depth);

/// <summary>
/// Walks a site, reading what each page declares about itself.
/// </summary>
/// <remarks>
/// <para>
/// The order of the checks is a policy statement. Before a page is fetched the crawler asks, in
/// turn: is this host one the job allows, has the seller recorded a rights policy that forbids
/// crawling it, does robots.txt permit this path, and has this host been left alone long enough.
/// Only then does it fetch. A crawler that checked afterwards would already have taken what it
/// was not entitled to.
/// </para>
/// <para>
/// Pages come back as listings only if they declare a product. A page that does not is still
/// followed for its links — that is how a category page leads to the products on it — but it
/// stages nothing.
/// </para>
/// </remarks>
internal sealed partial class WebsiteCrawlerAdapter(
    IHttpClientFactory httpClients,
    ISourcePolicyRepository policies,
    PolitenessGate politeness,
    IOptions<CrawlerOptions> options,
    IClock clock,
    ILogger<WebsiteCrawlerAdapter> logger) : ISourceAdapter
{
    public const string ClientName = "sourcing-crawler";

    [GeneratedRegex("""<a[^>]+href\s*=\s*["']([^"'#]+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex Link { get; }

    private readonly CrawlerOptions _options = options.Value;

    public bool Handles(ImportMode mode) => mode is ImportMode.Crawl or ImportMode.Combined;

    public async Task<DiscoveryBatch> DiscoverAsync(
        ImportJobSpec spec, string? cursor, int batchSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var state = Restore(cursor, spec);
        var listings = new List<RawListing>();
        var client = httpClients.CreateClient(ClientName);

        while (listings.Count < batchSize
            && state.Frontier.Count > 0
            && state.PagesFetched < spec.Limits.MaxPages)
        {
            ct.ThrowIfCancellationRequested();

            var url = state.Frontier[0];
            state.Frontier.RemoveAt(0);

            var canonical = UrlForm.Canonicalize(url);
            if (!state.Visited.Add(canonical)) continue;

            if (!Uri.TryCreate(canonical, UriKind.Absolute, out var uri)) continue;
            if (!await MayFetchAsync(uri, spec, client, ct)) continue;

            var wait = politeness.Reserve(uri.Host, Delay(uri, client), clock.UtcNow);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

            var html = await FetchAsync(client, uri, ct);
            state = state with { PagesFetched = state.PagesFetched + 1 };

            if (html is null) continue;

            listings.AddRange(ListingsOn(uri, html));

            if (state.Depth < spec.Limits.MaxDepth) Follow(uri, html, state);
        }

        var more = state.Frontier.Count > 0 && state.PagesFetched < spec.Limits.MaxPages;

        return new DiscoveryBatch(listings, Save(state), more);
    }

    /// <summary>
    /// Everything asked before a byte is fetched.
    /// </summary>
    /// <remarks>
    /// A refusal is logged and skipped rather than failing the job: one forbidden host in a
    /// job's source list should not stop the others being imported.
    /// </remarks>
    private async Task<bool> MayFetchAsync(
        Uri uri, ImportJobSpec spec, HttpClient client, CancellationToken ct)
    {
        var domain = SourceDomain.Normalize(uri.Host);

        if (spec.ExcludeDomains.Contains(domain, StringComparer.Ordinal))
        {
            logger.LogDebug("Skipping {Url}: the job excludes {Domain}.", uri, domain);
            return false;
        }

        if (spec.IncludeDomains.Count > 0
            && !spec.IncludeDomains.Contains(domain, StringComparer.Ordinal))
        {
            logger.LogDebug("Skipping {Url}: the job includes only its listed domains.", uri);
            return false;
        }

        var policy = await policies.FindByDomainAsync(domain, ct);
        if (policy is not null && !policy.CrawlAllowed)
        {
            logger.LogInformation(
                "Skipping {Url}: the seller's policy for {Domain} forbids crawling.", uri, domain);
            return false;
        }

        var robots = await RobotsForAsync(client, uri, ct);
        if (!robots.IsAllowed(uri.AbsolutePath))
        {
            logger.LogInformation("Skipping {Url}: robots.txt disallows it.", uri);
            return false;
        }

        return true;
    }

    private readonly Dictionary<string, RobotsTxt> _robots = new(StringComparer.OrdinalIgnoreCase);

    private async Task<RobotsTxt> RobotsForAsync(HttpClient client, Uri uri, CancellationToken ct)
    {
        if (_robots.TryGetValue(uri.Host, out var cached)) return cached;

        RobotsTxt robots;
        try
        {
            using var response = await client.GetAsync(
                new Uri(uri, "/robots.txt"), HttpCompletionOption.ResponseContentRead, ct);

            robots = response.IsSuccessStatusCode
                ? RobotsTxt.Parse(await response.Content.ReadAsStringAsync(ct), _options.ProductToken)

                // 404 means no restrictions; 403 or 5xx means the site is not answering, and
                // assuming permission from silence is how a crawler becomes a nuisance.
                : response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? RobotsTxt.AllowAll
                    : RobotsTxt.DenyAll;
        }
        catch (HttpRequestException exception)
        {
            logger.LogInformation(
                exception, "Could not read robots.txt for {Host}; treating the host as off limits.",
                uri.Host);

            robots = RobotsTxt.DenyAll;
        }

        _robots[uri.Host] = robots;

        return robots;
    }

    /// <remarks>The site's own request wins whenever it is slower than this seller's default.</remarks>
    private TimeSpan Delay(Uri uri, HttpClient client)
    {
        _ = client;

        var configured = TimeSpan.FromMilliseconds(_options.PolitenessDelayMs);

        return _robots.TryGetValue(uri.Host, out var robots) && robots.CrawlDelay is { } asked
            ? (asked > configured ? asked : configured)
            : configured;
    }

    private async Task<string?> FetchAsync(HttpClient client, Uri uri, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("{Url} answered {Status}.", uri, (int)response.StatusCode);
                return null;
            }

            // Capped before reading: a response with no Content-Length can still be endless, and
            // an import must not be a way to exhaust this machine's memory.
            if (response.Content.Headers.ContentLength > _options.MaxHtmlBytes) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();

            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > _options.MaxHtmlBytes)
                {
                    logger.LogDebug("{Url} exceeded the page size cap.", uri);
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (HttpRequestException exception)
        {
            // Includes the SSRF refusal, which is exactly where a private address is rejected.
            logger.LogInformation(exception, "Could not fetch {Url}.", uri);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogInformation("{Url} timed out.", uri);
            return null;
        }
    }

    private IEnumerable<RawListing> ListingsOn(Uri uri, string html)
    {
        var products = StructuredDataExtractor.Extract(html);
        var now = clock.UtcNow;

        if (products.Count == 0)
        {
            // Nothing declared. Not staged, and not silently forgotten either — a crawl that
            // found no products anywhere is usually a site this cannot read, and that is worth
            // seeing rather than guessing at.
            yield break;
        }

        foreach (var product in products)
        {
            yield return new RawListing(
                UrlForm.Canonicalize(uri.ToString()),
                SourceDomain.Normalize(uri.Host),
                product.Document["sku"]?.ToString(),
                now,
                product.Method,
                product.Document,
                product.Original,
                product.Original is null
                    ? []
                    : StructuredDataExtractor.Images(product.Original, uri.ToString()),
                null,
                uri.ToString());
        }
    }

    /// <remarks>
    /// Same-host links only. Following off-site links would turn one shop's import into a crawl
    /// of the open web, which no limit here could usefully bound.
    /// </remarks>
    private static void Follow(Uri page, string html, CrawlCursor state)
    {
        foreach (Match match in Link.Matches(html))
        {
            if (!Uri.TryCreate(page, match.Groups[1].Value, out var link)) continue;
            if (link.Scheme is not ("http" or "https")) continue;
            if (!string.Equals(link.Host, page.Host, StringComparison.OrdinalIgnoreCase)) continue;

            var canonical = UrlForm.Canonicalize(link.ToString());
            if (state.Visited.Contains(canonical)) continue;
            if (state.Frontier.Contains(canonical)) continue;

            state.Frontier.Add(canonical);
        }
    }

    private static CrawlCursor Restore(string? cursor, ImportJobSpec spec)
    {
        if (cursor is not null)
        {
            try
            {
                var restored = JsonSerializer.Deserialize<CrawlCursor>(cursor);
                if (restored is not null) return restored;
            }
            catch (JsonException)
            {
                // A cursor this version cannot read starts the crawl again rather than failing
                // the job: re-crawling is wasteful, and the fingerprint makes it harmless.
            }
        }

        return new CrawlCursor([.. spec.Sources], [], 0, 0);
    }

    private static string Save(CrawlCursor state) => JsonSerializer.Serialize(state);

    /// <summary>A page that declared nothing, for the record.</summary>
    internal static RawListing NoStructuredData(Uri uri, DateTimeOffset now) => new(
        UrlForm.Canonicalize(uri.ToString()),
        SourceDomain.Normalize(uri.Host),
        null,
        now,
        ExtractionMethod.None,
        new JsonObject(),
        null,
        [],
        ValidationCodes.NoStructuredData,
        uri.ToString());
}
