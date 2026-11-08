using System.Globalization;
using System.Text.Json.Nodes;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;

namespace EwaysVNext.Modules.Sourcing.Application.Discovery;

/// <summary>
/// The fields an uploaded file may map onto.
/// </summary>
/// <remarks>
/// A closed list, and mapping is explicit rather than guessed. A column silently matched to the
/// wrong field produces a catalogue that looks right and is not — and nobody reviews a column
/// heading they were never shown.
/// </remarks>
public static class ImportColumns
{
    public const string Name = "name";
    public const string Brand = "brand";
    public const string Description = "description";
    public const string Sku = "sku";
    public const string Gtin = "gtin";
    public const string Mpn = "mpn";
    public const string Price = "price";
    public const string Currency = "currency";
    public const string Category = "category";
    public const string ImageUrl = "image_url";
    public const string SourceUrl = "source_url";
    public const string SourceDomain = "source_domain";
    public const string SourceId = "source_id";

    /// <summary>A column the uploader has said to ignore.</summary>
    public const string Ignore = "ignore";

    /// <summary>Prefix for a catalogue attribute, as <c>attr:colour</c>.</summary>
    public const string AttributePrefix = "attr:";

    public static IReadOnlyList<string> All { get; } =
    [
        Name, Brand, Description, Sku, Gtin, Mpn, Price, Currency, Category,
        ImageUrl, SourceUrl, SourceDomain, SourceId,
    ];

    public static bool IsKnown(string field) =>
        field == Ignore
        || All.Contains(field, StringComparer.Ordinal)
        || field.StartsWith(AttributePrefix, StringComparison.Ordinal);
}

/// <summary>
/// Turns one file row into the shape every adapter produces.
/// </summary>
/// <remarks>
/// <para>
/// Pure, so the whole file path can be tested without a file. The mapping decides which cell
/// means what; this decides what the result looks like, and the two are kept apart because the
/// mapping changes per upload and the shape never does.
/// </para>
/// <para>
/// The <c>urn:</c> source URL is provenance only. It names the job and the row so a reviewer can
/// find the line in the spreadsheet, and the fingerprint deliberately ignores it — otherwise
/// re-uploading one file would create a second set of products.
/// </para>
/// </remarks>
public static class ImportRowMapper
{
    public static RawListing Map(
        long jobId,
        int rowNumber,
        IReadOnlyDictionary<string, string?> row,
        ImportColumnMapping mapping,
        ExtractionMethod method,
        DateTimeOffset collectedAt)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mapping);

        var document = new JsonObject();
        var attributes = new JsonObject();
        var images = new List<string>();

        string? sourceUrl = null;
        string? sourceDomain = null;
        string? sourceId = null;
        string? price = null;
        string? currency = null;

        foreach (var (column, raw) in row)
        {
            if (!mapping.SourceToField.TryGetValue(column, out var field)) continue;
            if (field == ImportColumns.Ignore) continue;

            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value)) continue;

            if (field.StartsWith(ImportColumns.AttributePrefix, StringComparison.Ordinal))
            {
                attributes[field[ImportColumns.AttributePrefix.Length..]] = value;
                continue;
            }

            switch (field)
            {
                case ImportColumns.Price: price = value; break;
                case ImportColumns.Currency: currency = value.ToUpperInvariant(); break;
                case ImportColumns.SourceUrl: sourceUrl = value; break;
                case ImportColumns.SourceDomain: sourceDomain = value; break;
                case ImportColumns.SourceId: sourceId = value; break;

                // Several image columns are normal in a supplier's file; they accumulate in the
                // order the columns appeared.
                case ImportColumns.ImageUrl: images.Add(value); break;

                default: document[field] = value; break;
            }
        }

        if (price is not null)
        {
            // schema.org's own shape, so the normalizer reads a file row and a crawled page the
            // same way.
            document["offers"] = new JsonObject
            {
                ["price"] = price,
                ["priceCurrency"] = currency,
            };
        }

        if (attributes.Count > 0) document["additionalProperty"] = attributes;

        return new RawListing(
            sourceUrl ?? Urn(jobId, rowNumber),
            sourceDomain is null ? null : Domain.SourcePolicies.SourceDomain.Normalize(sourceDomain),
            sourceId,
            collectedAt,
            method,
            document,
            Original(row),
            [.. images.Distinct(StringComparer.Ordinal)],
            null,
            rowNumber.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Where a reviewer looks to find this line in the uploaded file.</summary>
    public static string Urn(long jobId, int rowNumber) =>
        $"urn:eways:import:{jobId.ToString(CultureInfo.InvariantCulture)}:row:"
        + rowNumber.ToString(CultureInfo.InvariantCulture);

    /// <remarks>
    /// The row exactly as read, including the columns that were ignored. When somebody disputes
    /// what was published, the answer is what the file said, not what the mapping made of it.
    /// </remarks>
    private static JsonObject Original(IReadOnlyDictionary<string, string?> row)
    {
        var original = new JsonObject();

        foreach (var (column, value) in row) original[column] = value;

        return original;
    }
}
