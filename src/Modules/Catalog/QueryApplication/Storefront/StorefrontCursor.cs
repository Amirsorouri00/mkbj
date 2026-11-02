using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Storefront;

public enum StorefrontSort
{
    Relevance,
    Newest,
    PriceAsc,
    PriceDesc,
    Popular,
}

/// <summary>
/// Where the previous page stopped: the sort key's value and the product id that broke its tie.
/// </summary>
/// <remarks>
/// <para>
/// Keyset, not offset. A catalogue changes between two page requests, and <c>OFFSET</c> answers
/// the second one from a list that has shifted — so a visitor paging forward silently skips
/// products, or sees one twice. A key says "after this exact row", which stays true.
/// </para>
/// <para>
/// It carries the <c>Sort</c> and <c>Mode</c> it was made under so a cursor cannot be replayed
/// against a different ordering, where the key would mean nothing. It is opaque to a client but
/// not secret: it holds nothing a visitor could not read off the page it came from.
/// </para>
/// </remarks>
public sealed record StorefrontCursor(string Mode, StorefrontSort Sort, string Key, long ProductId)
{
    /// <summary>Full text search; the other is <c>trgm</c>, the typo fallback.</summary>
    public const string FtsMode = "fts";

    public const string TrigramMode = "trgm";

    /// <summary>Beyond this, it is not a cursor this shop issued.</summary>
    private const int MaxLength = 512;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Encode() =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(this, Json));

    /// <summary>
    /// Decodes a cursor, or reports that it is not one. Never throws: the input is a query-string
    /// value from the internet, and a malformed one is an ordinary 400, not an exception.
    /// </summary>
    public static bool TryDecode(string? text, out StorefrontCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength) return false;

        try
        {
            var decoded = JsonSerializer.Deserialize<StorefrontCursor>(
                Base64Url.DecodeFromChars(text), Json);

            // A cursor whose mode or key is missing decodes as a record with nulls, which would
            // then be used as a key. Rejecting it here keeps every caller from re-checking.
            if (decoded is null || string.IsNullOrEmpty(decoded.Mode) || decoded.Key is null)
                return false;

            if (decoded.Mode is not (FtsMode or TrigramMode)) return false;
            if (!Enum.IsDefined(decoded.Sort)) return false;

            cursor = decoded;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }
}
