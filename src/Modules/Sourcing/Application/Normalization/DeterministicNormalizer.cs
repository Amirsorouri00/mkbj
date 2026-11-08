using System.Text;
using System.Text.Json.Nodes;
using EwaysVNext.Modules.Sourcing.Application.Discovery;

namespace EwaysVNext.Modules.Sourcing.Application.Normalization;

/// <summary>
/// What a listing turned out to be, in this system's own terms.
/// </summary>
/// <remarks>
/// Every field carries where it came from: <see cref="Provenance"/> maps a field name to an RFC
/// 6901 pointer into the raw document. A published product that nobody can trace back to a line
/// in a supplier's file is one nobody can defend when it is disputed.
/// </remarks>
public sealed record NormalizedProduct(
    string? Name,
    string? Brand,
    string? Description,
    string? Sku,
    string? Gtin,
    string? Mpn,
    long? PriceMinor,
    string? Currency,
    string? Category,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, string> Provenance)
{
    /// <summary>
    /// The signature that distinguishes one variant of a listing from another.
    /// </summary>
    /// <remarks>
    /// Built from the attributes that vary, in sorted order so the same variant always hashes
    /// the same whatever order the source listed them in.
    /// </remarks>
    public string VariantKey()
    {
        if (Attributes.Count == 0) return "default";

        var builder = new StringBuilder();

        foreach (var (key, value) in Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            builder.Append(key).Append('=').Append(value).Append(';');

        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(builder.ToString())))[..32];
    }
}

/// <summary>
/// Turns a raw listing into a normalized product, the same way every time.
/// </summary>
/// <remarks>
/// <para>
/// Pure and deterministic, which is the point of the name. The same raw document always
/// produces the same normalized product, so a re-import changes nothing unless the source
/// changed — and a reviewer looking at a record months later sees what the pipeline saw.
/// </para>
/// <para>
/// Nothing is invented. A field the source did not provide stays null and becomes a validation
/// issue; guessing a brand from a product title is how a catalogue fills up with brands that do
/// not exist.
/// </para>
/// </remarks>
public static class DeterministicNormalizer
{
    public static NormalizedProduct Normalize(RawListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        var document = listing.Document;
        var provenance = new Dictionary<string, string>(StringComparer.Ordinal);

        var name = Text(document, "name", provenance, nameof(NormalizedProduct.Name));
        var brand = Text(document, "brand", provenance, nameof(NormalizedProduct.Brand));
        var description = Text(document, "description", provenance, nameof(NormalizedProduct.Description));
        var sku = Text(document, "sku", provenance, nameof(NormalizedProduct.Sku));
        var mpn = Text(document, "mpn", provenance, nameof(NormalizedProduct.Mpn));
        var category = Text(document, "category", provenance, nameof(NormalizedProduct.Category));

        var gtin = Gtin(document, provenance);
        var (priceMinor, currency) = Offer(document, provenance);

        return new NormalizedProduct(
            Collapse(name),
            Collapse(brand),
            description,
            Collapse(sku),
            gtin,
            Collapse(mpn),
            priceMinor,
            currency,
            Collapse(category),
            [.. listing.ImageUrls],
            Attributes(document, provenance),
            provenance);
    }

    /// <summary>
    /// The first GTIN that passes its check digit.
    /// </summary>
    /// <remarks>
    /// An invalid barcode is dropped rather than carried: it is the matcher's strongest signal,
    /// and a wrong one attaches a listing to somebody else's product. The validator raises a
    /// warning so the reviewer knows the source had one and it did not add up.
    /// </remarks>
    private static string? Gtin(JsonObject document, Dictionary<string, string> provenance)
    {
        foreach (var key in new[] { "gtin", "gtin14", "gtin13", "gtin12", "gtin8" })
        {
            if (document[key] is null) continue;

            var normalized = GtinValidator.Normalize(document[key]!.ToString());
            if (normalized is null) continue;

            provenance[nameof(NormalizedProduct.Gtin)] = $"/{key}";

            return normalized;
        }

        return null;
    }

    private static (long? PriceMinor, string? Currency) Offer(
        JsonObject document, Dictionary<string, string> provenance)
    {
        // schema.org allows one offer or many; the first is the one a shop shows.
        var offer = document["offers"] switch
        {
            JsonObject single => single,
            JsonArray many when many.Count > 0 => many[0] as JsonObject,
            _ => null,
        };

        if (offer is null) return (null, null);

        var prefix = document["offers"] is JsonArray ? "/offers/0" : "/offers";

        var priceMinor = PriceParser.ToMinor(offer["price"]?.ToString());
        if (priceMinor is not null)
            provenance[nameof(NormalizedProduct.PriceMinor)] = $"{prefix}/price";

        var currency = Collapse(offer["priceCurrency"]?.ToString())?.ToUpperInvariant();
        if (currency is not null)
            provenance[nameof(NormalizedProduct.Currency)] = $"{prefix}/priceCurrency";

        return (priceMinor, currency);
    }

    private static Dictionary<string, string> Attributes(
        JsonObject document, Dictionary<string, string> provenance)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (document["additionalProperty"] is not JsonObject properties) return attributes;

        foreach (var (key, value) in properties)
        {
            var text = Collapse(value?.ToString());
            if (text is null) continue;

            attributes[key] = text;
            provenance[$"attr:{key}"] = $"/additionalProperty/{key}";
        }

        return attributes;
    }

    private static string? Text(
        JsonObject document, string key, Dictionary<string, string> provenance, string field)
    {
        var node = document[key];
        if (node is null) return null;

        // schema.org lets a brand be a string or an object with a name.
        var value = node is JsonObject nested ? nested["name"]?.ToString() : node.ToString();
        if (string.IsNullOrWhiteSpace(value)) return null;

        provenance[field] = node is JsonObject ? $"/{key}/name" : $"/{key}";

        return value;
    }

    /// <summary>
    /// Trims and collapses runs of whitespace.
    /// </summary>
    /// <remarks>
    /// Scraped text is full of newlines and double spaces from the page's own layout. They are
    /// not part of the name, and leaving them in means two listings of one product differ by
    /// invisible characters.
    /// </remarks>
    private static string? Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
        }

        var result = builder.ToString();

        return result.Length == 0 ? null : result;
    }
}
