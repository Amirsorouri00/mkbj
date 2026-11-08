using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Search;

public sealed class BraveOptions
{
    public const string SectionName = "Brave";

    /// <summary>
    /// Absent by default, and that disables search rather than failing it.
    /// </summary>
    /// <remarks>
    /// A seller who has not bought a search subscription should still be able to import from
    /// files and crawls. The key is a secret and never has a default.
    /// </remarks>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.search.brave.com";

    /// <summary>Results per query. Brave caps a page at 20.</summary>
    public int ResultsPerQuery { get; set; } = 20;
}

/// <summary>One page the search engine thinks is relevant.</summary>
public sealed record SearchHit(string Url, string Title);

/// <summary>
/// Finding candidate product pages by asking a search engine.
/// </summary>
/// <remarks>
/// A port, so the engine is replaceable and the pipeline never learns which one answered. What
/// comes back is a list of <b>URLs to look at</b>, never product data — a search snippet is a
/// summary written for a reader, and importing one would put somebody else's editorial text in
/// this catalogue as though it were a product description.
/// </remarks>
public interface ISearchProvider
{
    bool IsEnabled { get; }

    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query, int offset, CancellationToken ct = default);
}

/// <remarks>
/// <b>Not exercised against the live Brave service by any test in this repository.</b> The
/// response mapping is unit-tested against a recorded payload shape, which catches a rename in
/// this code but not a change at the far end. Treat the first live run as the real test.
/// </remarks>
internal sealed class BraveSearchProvider(
    IHttpClientFactory httpClients,
    IOptions<BraveOptions> options,
    ILogger<BraveSearchProvider> logger) : ISearchProvider
{
    public const string ClientName = "sourcing-brave";

    private readonly BraveOptions _options = options.Value;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query, int offset, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            logger.LogInformation("Web search is disabled: no Brave API key is configured.");
            return [];
        }

        var client = httpClients.CreateClient(ClientName);

        var url = $"/res/v1/web/search?q={Uri.EscapeDataString(query)}"
            + $"&count={_options.ResultsPerQuery}&offset={offset}";

        try
        {
            using var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                // A search that will not answer is not a reason to fail an import: the crawl
                // sources in the same job are still worth reading.
                logger.LogWarning(
                    "Brave answered {Status} for a search.", (int)response.StatusCode);

                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<BraveResponse>(ct);

            return Map(payload);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not reach Brave.");
            return [];
        }
    }

    /// <summary>Internal so the mapping can be tested without an HTTP call.</summary>
    internal static IReadOnlyList<SearchHit> Map(BraveResponse? payload) =>
        payload?.Web?.Results is not { } results
            ? []
            : [.. results
                .Where(result => !string.IsNullOrWhiteSpace(result.Url))
                .Select(result => new SearchHit(result.Url!, result.Title ?? string.Empty))];

    internal sealed record BraveResponse([property: JsonPropertyName("web")] BraveWeb? Web);

    internal sealed record BraveWeb(
        [property: JsonPropertyName("results")] IReadOnlyList<BraveResult>? Results);

    internal sealed record BraveResult(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("title")] string? Title);
}
