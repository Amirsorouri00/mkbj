using System.Collections.Specialized;
using System.Text;
using System.Web;

namespace EwaysVNext.Modules.Sourcing.Application.Discovery;

/// <summary>
/// The one spelling of a URL that two links to the same page agree on.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and in Application rather than in the crawler, because two separate things depend on
/// agreeing exactly: the fingerprint that decides whether a listing has been seen before, and
/// the crawler's own visited set. Two implementations of "the same page" would mean a product
/// staged twice from one site.
/// </para>
/// <para>
/// What is dropped is what does not identify the page: the fragment (a position within it), the
/// default port, case in the scheme and host, and campaign parameters. Query keys are sorted so
/// that <c>?a=1&amp;b=2</c> and <c>?b=2&amp;a=1</c> are one URL. Everything else is kept —
/// plenty of shops put the product id in a query parameter.
/// </para>
/// </remarks>
public static class UrlForm
{
    /// <summary>
    /// Parameters that track a visit rather than name a page.
    /// </summary>
    /// <remarks>
    /// Matched by prefix for <c>utm_</c>, because the family keeps growing and a list of exact
    /// names would go stale the first time a marketing tool invented another one.
    /// </remarks>
    private static readonly string[] TrackingKeys =
        ["gclid", "fbclid", "msclkid", "mc_cid", "mc_eid", "igshid", "ref", "ref_src"];

    public static string Canonicalize(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url.Trim();

        var builder = new StringBuilder()
            .Append(uri.Scheme.ToLowerInvariant())
            .Append("://")
            .Append(uri.Host.ToLowerInvariant());

        if (!uri.IsDefaultPort) builder.Append(':').Append(uri.Port);

        builder.Append(uri.AbsolutePath.TrimEnd('/') is { Length: > 0 } path ? path : "/");

        var query = Query(uri);
        if (query.Length > 0) builder.Append('?').Append(query);

        return builder.ToString();
    }

    private static string Query(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query)) return string.Empty;

        NameValueCollection parsed = HttpUtility.ParseQueryString(uri.Query);

        var kept = parsed.AllKeys
            .Where(key => key is not null)
            .Where(key => !IsTracking(key!))
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => $"{Uri.EscapeDataString(key!)}={Uri.EscapeDataString(parsed[key] ?? string.Empty)}");

        return string.Join('&', kept);
    }

    private static bool IsTracking(string key) =>
        key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
        || TrackingKeys.Contains(key, StringComparer.OrdinalIgnoreCase);
}
