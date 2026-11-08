using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EwaysVNext.Modules.Sourcing.Application.Discovery;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Crawling;

/// <summary>One product found on one page, with how it was found.</summary>
public sealed record ExtractedProduct(JsonObject Document, ExtractionMethod Method, JsonObject? Original);

/// <summary>
/// Pulls product data out of a page the way the page itself declares it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only declared structured data is read — never the visible text.</b> Scraping a page's
/// layout produces a catalogue that breaks whenever the shop changes its theme, and it reads
/// things the shop never meant as product data. JSON-LD and OpenGraph are the shop's own
/// statement of what the page is about, which is a far better thing to rely on and a far easier
/// thing to defend.
/// </para>
/// <para>
/// <b>Microdata is not supported.</b> Reading it properly needs a full HTML parser, and a
/// half-working one would silently mis-read pages rather than skip them. A page with neither
/// JSON-LD nor OpenGraph comes back as <c>NO_STRUCTURED_DATA</c>, which is honest and visible in
/// the review queue.
/// </para>
/// </remarks>
public static partial class StructuredDataExtractor
{
    [GeneratedRegex(
        """<script[^>]*type\s*=\s*["']application/ld\+json["'][^>]*>(.*?)</script>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLdBlock { get; }

    /// <remarks>
    /// Both the <c>og:</c> and <c>product:</c> families are read. A shop writes its title under
    /// <c>og:title</c> and its price under <c>product:price:amount</c>, and matching only the
    /// first family finds a product with no price — which validation then refuses, leaving a
    /// reviewer to wonder why the page they can see a price on produced none.
    /// </remarks>
    [GeneratedRegex(
        """<meta[^>]*?(?:property|name)\s*=\s*["'](og|product):([a-z:]+)["'][^>]*?content\s*=\s*["']([^"']*)["'][^>]*>""",
        RegexOptions.IgnoreCase)]
    private static partial Regex OpenGraphMeta { get; }

    /// <remarks>Attribute order is not fixed in HTML, and plenty of templates write content first.</remarks>
    [GeneratedRegex(
        """<meta[^>]*?content\s*=\s*["']([^"']*)["'][^>]*?(?:property|name)\s*=\s*["'](og|product):([a-z:]+)["'][^>]*>""",
        RegexOptions.IgnoreCase)]
    private static partial Regex OpenGraphMetaReversed { get; }

    public static IReadOnlyList<ExtractedProduct> Extract(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];

        var products = JsonLd(html);

        // OpenGraph only when the page declared no product in JSON-LD: it carries far less, and
        // preferring it would throw away the better data when both are present.
        return products.Count > 0 ? products : OpenGraph(html);
    }

    private static List<ExtractedProduct> JsonLd(string html)
    {
        var products = new List<ExtractedProduct>();

        foreach (Match block in JsonLdBlock.Matches(html))
        {
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(block.Groups[1].Value);
            }
            catch (JsonException)
            {
                // A malformed block on one page is not worth failing the crawl over; a shop
                // with broken JSON-LD on one template still has good data on the next page.
                continue;
            }

            foreach (var node in Flatten(parsed))
            {
                if (node is not JsonObject candidate) continue;
                if (!IsProduct(candidate)) continue;

                products.Add(new ExtractedProduct(
                    Normalize(candidate),
                    ExtractionMethod.JsonLd,
                    candidate.DeepClone() as JsonObject));
            }
        }

        return products;
    }

    /// <summary>
    /// Walks arrays and <c>@graph</c> containers to the nodes themselves.
    /// </summary>
    /// <remarks>
    /// Real pages wrap their product in a graph with the organisation, the breadcrumb and the
    /// website. Looking only at the top level finds nothing on those pages.
    /// </remarks>
    private static IEnumerable<JsonNode?> Flatten(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    foreach (var nested in Flatten(item)) yield return nested;
                }

                break;

            case JsonObject item when item["@graph"] is { } graph:
                foreach (var nested in Flatten(graph)) yield return nested;

                // The wrapper itself may still be a product.
                yield return item;
                break;

            default:
                yield return node;
                break;
        }
    }

    private static bool IsProduct(JsonObject node)
    {
        var type = node["@type"];

        return type switch
        {
            JsonArray types => types.Any(value => IsProductType(value?.ToString())),
            null => false,
            _ => IsProductType(type.ToString()),
        };
    }

    private static bool IsProductType(string? type) =>
        type is not null
        && (type.Equals("Product", StringComparison.OrdinalIgnoreCase)
            || type.Equals("ProductGroup", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Keeps the fields the normalizer reads, in the shape it expects.
    /// </summary>
    /// <remarks>
    /// The original node is kept separately and untouched. This is a convenience copy, not a
    /// replacement — when somebody disputes what was published, the answer is what the page
    /// said.
    /// </remarks>
    private static JsonObject Normalize(JsonObject node)
    {
        var document = new JsonObject();

        foreach (var key in new[]
                 {
                     "name", "description", "sku", "mpn",
                     "gtin", "gtin8", "gtin12", "gtin13", "gtin14", "category",
                 })
        {
            if (node[key] is { } value) document[key] = value.DeepClone();
        }

        if (node["brand"] is { } brand) document["brand"] = brand.DeepClone();
        if (node["offers"] is { } offers) document["offers"] = offers.DeepClone();

        return document;
    }

    private static List<ExtractedProduct> OpenGraph(string html)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in OpenGraphMeta.Matches(html))
            tags.TryAdd(Key(match.Groups[1].Value, match.Groups[2].Value), match.Groups[3].Value);

        foreach (Match match in OpenGraphMetaReversed.Matches(html))
            tags.TryAdd(Key(match.Groups[2].Value, match.Groups[3].Value), match.Groups[1].Value);

        if (!tags.TryGetValue("type", out var type)
            || !type.Contains("product", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (!tags.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title))
            return [];

        var document = new JsonObject { ["name"] = title };

        if (tags.TryGetValue("description", out var description))
            document["description"] = description;

        if (tags.TryGetValue("product:price:amount", out var price))
        {
            document["offers"] = new JsonObject
            {
                ["price"] = price,
                ["priceCurrency"] = tags.GetValueOrDefault("product:price:currency"),
            };
        }

        var original = new JsonObject();
        foreach (var (key, value) in tags) original[key] = value;

        return [new ExtractedProduct(document, ExtractionMethod.OpenGraph, original)];
    }

    /// <summary>
    /// The key a tag is stored under: bare for <c>og:</c>, prefixed for everything else.
    /// </summary>
    /// <remarks>
    /// <c>og:title</c> is "title" because OpenGraph is the family this reads by default, while
    /// <c>product:price:amount</c> keeps its prefix so it cannot collide with an og tag of the
    /// same tail.
    /// </remarks>
    private static string Key(string family, string name) =>
        family.Equals("og", StringComparison.OrdinalIgnoreCase) ? name : $"{family}:{name}";

    /// <summary>Every image URL the page declared, in document order.</summary>
    public static IReadOnlyList<string> Images(JsonObject original, string pageUrl)
    {
        var images = new List<string>();

        void Add(JsonNode? node)
        {
            switch (node)
            {
                case JsonArray array:
                    foreach (var item in array) Add(item);
                    break;

                case JsonObject image when image["url"] is { } url:
                    Add(url);
                    break;

                case null:
                    break;

                default:
                    // Relative URLs are normal in page markup and useless once stored.
                    if (Uri.TryCreate(new Uri(pageUrl), node.ToString(), out var absolute)
                        && absolute.Scheme is "http" or "https")
                    {
                        images.Add(absolute.ToString());
                    }

                    break;
            }
        }

        Add(original["image"]);
        Add(original["og:image"]);
        Add(original["image:secure_url"]);

        return [.. images.Distinct(StringComparer.Ordinal)];
    }
}
