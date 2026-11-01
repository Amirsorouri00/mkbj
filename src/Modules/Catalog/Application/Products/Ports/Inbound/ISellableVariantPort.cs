namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

/// <summary>
/// One buyable variant, as everything outside the catalogue sees it.
/// </summary>
/// <remarks>
/// <c>CategoryPath</c> runs root to leaf — "Kitchen", "Kettles" — because that is the order a
/// breadcrumb and an invoice line both read in. <c>Attributes</c> is the variant's display
/// values, keyed by canonical key.
/// </remarks>
public sealed record SellableVariant(
    long VariantId,
    long ProductId,
    string Sku,
    string Title,
    long BrandId,
    string BrandName,
    long PrimaryCategoryId,
    IReadOnlyList<string> CategoryPath,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>
/// What the catalogue says is sellable. Deliberately says nothing about price — whether a variant
/// can be bought at all is the catalogue's half of the question, and what it costs is Pricing's.
/// </summary>
public interface ISellableVariantPort
{
    /// <summary>
    /// The sellable variants, optionally narrowed to a set of ids. Null means all of them, which
    /// is what a full storefront rebuild asks for.
    /// </summary>
    Task<IReadOnlyList<SellableVariant>> GetSellableAsync(
        IReadOnlyCollection<long>? variantIds = null, CancellationToken ct = default);

    /// <summary>
    /// The version this answer was computed against. A caller that holds an older number knows
    /// its copy may be stale without having to compare the data itself.
    /// </summary>
    Task<long> GetVersionAsync(CancellationToken ct = default);
}
