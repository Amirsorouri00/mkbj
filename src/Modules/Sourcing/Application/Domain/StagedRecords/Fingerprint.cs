using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EwaysVNext.Modules.Sourcing.Application.Discovery;

namespace EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;

/// <summary>
/// What makes two listings the same product.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of "import twice, stage once". Get it too narrow and every re-import
/// duplicates the catalogue; too wide and two different products collapse into one. So the
/// identity is taken from the strongest thing available, in order, and never from anything that
/// merely describes <b>where</b> the listing was found this time.
/// </para>
/// <para>
/// The order is: the source's own id on its own domain, then the canonical URL, then a
/// manufacturer identifier, then the brand and name together. A file row's synthetic
/// <c>urn:</c> is never used — it contains the job id and the row number, so fingerprinting it
/// would make every re-upload of the same spreadsheet a fresh set of products.
/// </para>
/// <para>
/// The identity string is NFKC-normalized before hashing, so two spellings that a reader cannot
/// tell apart do not become two products.
/// </para>
/// </remarks>
public sealed record Fingerprint(string Value)
{
    public static Fingerprint For(RawListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        return new Fingerprint(Hash(Identity(listing)));
    }

    private static string Identity(RawListing listing)
    {
        // Strongest: the shop's own product id, scoped to the shop. Two URLs on one site that
        // carry the same sku are one product however differently they are linked.
        if (!string.IsNullOrWhiteSpace(listing.SourceDomain)
            && !string.IsNullOrWhiteSpace(listing.SourceId))
        {
            return $"web|{listing.SourceDomain}|{listing.SourceId}";
        }

        // Next: the page itself, in the one spelling two links to it agree on.
        if (Uri.TryCreate(listing.SourceUrl, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            return $"url|{UrlForm.Canonicalize(listing.SourceUrl)}";
        }

        // A file row, whose URL is synthetic. Identity has to come from the content.
        if (Gtin(listing.Document) is { } gtin) return $"gtin|{gtin}";

        var brand = NameKey(Text(listing.Document, "brand"));
        var mpn = Text(listing.Document, "mpn");

        if (!string.IsNullOrWhiteSpace(mpn)) return $"mpn|{brand}|{mpn}";

        return $"title|{brand}|{NameKey(Text(listing.Document, "name"))}";
    }

    /// <summary>The first usable GTIN of any length, digits only.</summary>
    private static string? Gtin(JsonObject document)
    {
        foreach (var key in new[] { "gtin", "gtin14", "gtin13", "gtin12", "gtin8" })
        {
            var value = Text(document, key);
            if (string.IsNullOrWhiteSpace(value)) continue;

            var digits = new string([.. value.Where(char.IsAsciiDigit)]);
            if (digits.Length > 0) return digits;
        }

        return null;
    }

    /// <summary>
    /// A name reduced to the part that identifies it.
    /// </summary>
    /// <remarks>
    /// Case, punctuation and runs of whitespace carry no identity: "Acme Kettle — 1.7L" and
    /// "acme kettle 1.7l" are one product, and treating them as two is how a re-import doubles
    /// a catalogue.
    /// </remarks>
    private static string NameKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var ch in value.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace && builder.Length > 0)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string? Text(JsonObject document, string key)
    {
        if (!document.TryGetPropertyValue(key, out var node) || node is null) return null;

        // schema.org lets a brand be either a string or an object with a name.
        if (node is JsonObject nested) return nested["name"]?.GetValue<string>();

        return node.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? node.GetValue<string>()
            : node.ToString();
    }

    private static string Hash(string identity) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity.Normalize(NormalizationForm.FormKC))));
}
