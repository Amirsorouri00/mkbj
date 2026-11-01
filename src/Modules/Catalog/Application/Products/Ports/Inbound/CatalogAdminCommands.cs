using EwaysVNext.Modules.Catalog.Application.Products.Domain;

namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

/// <remarks>
/// A null <c>Slug</c> lets the product take <c>{canonical-title}-{id}</c>. Two products
/// legitimately share a title ("Black T-Shirt") and the slug is globally unique, so the id is
/// what separates them.
/// </remarks>
public sealed record CreateProductCommand(
    long BrandId,
    long PrimaryCategoryId,
    string Title,
    string? Slug,
    string? ShortDescription,
    string? Description);

/// <remarks>
/// There is no slug here on purpose: it is a URL customers have bookmarked and search engines
/// have indexed, so retitling a product does not move it.
/// </remarks>
public sealed record UpdateProductCommand(
    long ProductId,
    long BrandId,
    long PrimaryCategoryId,
    string Title,
    string? ShortDescription,
    string? Description);

/// <remarks>
/// A null <c>Sku</c> takes <c>SKU-{variantId}</c>, which is unique because the id is.
/// </remarks>
public sealed record AddVariantCommand(
    long ProductId,
    string? Sku,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record IdentifierInput(
    ProductIdentifierType Type,
    string Value,
    string? SourceDomain);

public enum BulkStatusChange
{
    None,
    Activate,
    Deactivate
}

/// <summary>
/// One edit applied to many products. Capped at 500 ids: beyond that an admin is describing a
/// query, not a selection, and a single save holding thousands of aggregates is a lock held far
/// too long.
/// </summary>
public sealed record BulkProductEdit(
    IReadOnlyList<long> ProductIds,
    BulkStatusChange Status,
    long? PrimaryCategoryId,
    long? BrandId);

/// <remarks><c>ErrorCode</c> is null when the item succeeded.</remarks>
public sealed record BulkItemOutcome(long ProductId, bool Succeeded, string? ErrorCode);

/// <summary>
/// Per-item outcomes rather than one verdict: a bulk edit where one product cannot go active is
/// a normal day, and failing all 500 because of it would make the feature useless.
/// </summary>
public sealed record BulkEditResult(IReadOnlyList<BulkItemOutcome> Items);
