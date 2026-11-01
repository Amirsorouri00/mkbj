using System.Security.Cryptography;
using System.Text;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Common;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

/// <summary>
/// The canonical form of a variant's distinguishing attributes, and the hash that identifies it.
/// <para>
/// This is a wire format, not an implementation detail: phase 7's sourcing module computes the
/// same hash from a crawled listing, and the two must agree exactly or a variant already in the
/// catalogue is imported again as a new one. The sort, the escaping and the whitespace collapse
/// are all part of that contract.
/// </para>
/// </summary>
/// <param name="Text">The canonical text the hash is taken over.</param>
/// <param name="Hash">Lower-hex SHA-256 of <paramref name="Text"/> in UTF-8.</param>
/// <param name="Values">
/// The display values, keyed by canonical key — what gets stored as the variant's attribute jsonb
/// and shown to a customer. These keep the supplier's capitalisation; only the hash ignores it.
/// </param>
public sealed record AttributeSignature(
    string Text,
    string Hash,
    IReadOnlyDictionary<string, string> Values)
{
    /// <summary>A variant with no distinguishing attributes — the only variant of its product.</summary>
    public static readonly AttributeSignature Empty = From(new Dictionary<string, string>());

    public static AttributeSignature From(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (rawKey, rawValue) in attributes)
        {
            var key = CanonicalKey.From(rawKey);
            var value = NormalizeDisplayValue(rawValue);

            // Two raw keys can canonicalise alike ("Size" and "size"). Silently keeping one would
            // make the result depend on dictionary ordering, so it is refused instead.
            if (!values.TryAdd(key, value))
            {
                throw new DomainException(
                    "VARIANT_ATTRIBUTE_DUPLICATE_KEY",
                    $"Two attributes resolve to the key '{key}'.");
            }
        }

        var text = string.Join(';', values.Select(pair =>
            $"{pair.Key}={Escape(pair.Value.ToLowerInvariant())}"));

        return new AttributeSignature(
            text,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
            values);
    }

    /// <summary>
    /// The hash alone. Named <c>HashOf</c> because a static method cannot share a name with the
    /// record's own <see cref="Hash"/> property.
    /// </summary>
    public static string HashOf(IReadOnlyDictionary<string, string> attributes) => From(attributes).Hash;

    private static string NormalizeDisplayValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(
                "VARIANT_ATTRIBUTE_VALUE_EMPTY", "A variant attribute value cannot be blank.");
        }

        // Collapse runs of whitespace so "Deep  Red" and "Deep Red" are one variant, then compose
        // so an accent written as a combining mark hashes the same as the precomposed character.
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Escapes the two separators and the escape character itself. <c>%</c> goes first, so a
    /// literal "%3b" a supplier typed comes back as "%253b" and stays distinguishable from a real
    /// semicolon — reversing the order would make them collide.
    /// </summary>
    private static string Escape(string value) => value
        .Replace("%", "%25", StringComparison.Ordinal)
        .Replace("=", "%3d", StringComparison.Ordinal)
        .Replace(";", "%3b", StringComparison.Ordinal);
}
