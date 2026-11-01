using EwaysVNext.Modules.Catalog.Application.Products.Domain;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Products;

/// <remarks><c>Q</c> is matched against the title, the slug and the MPN.</remarks>
public sealed record ProductAdminFilter(
    string? Q = null,
    ProductStatus? Status = null,
    long? BrandId = null,
    long? CategoryId = null);

/// <summary>One row of the admin product list — enough to decide which product to open.</summary>
public sealed record ProductAdminRowDto(
    long Id,
    string Title,
    string Slug,
    string Status,
    bool DeactivatedByAdmin,
    long BrandId,
    string BrandName,
    long PrimaryCategoryId,
    string PrimaryCategoryName,
    string? Mpn,
    int VariantCount,
    int ActiveVariantCount,
    bool HasUsableImage,
    long? MergedIntoProductId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ProductAdminVariantDto(
    long Id,
    string Sku,
    string AttributeSignature,
    IReadOnlyDictionary<string, string> AttributeValues,
    bool IsActive,
    bool DeactivatedByAdmin);

public sealed record ProductAdminIdentifierDto(
    long Id,
    string Type,
    string Value,
    string? SourceDomain);

/// <remarks>
/// <c>Url</c> is built from <c>Storefront:MediaBaseUrl</c> and the object key. The key is
/// returned too, since the admin UI needs the stable name and the URL may be reconfigured.
/// </remarks>
public sealed record ProductAdminMediaDto(
    long Id,
    string ObjectKey,
    string Url,
    int SortOrder,
    string? Alt,
    string UsageRights,
    bool IsUsable);

/// <remarks>
/// An empty <c>ActivationProblems</c> means the product can go active. It is returned so an admin
/// sees why the button is refused before they press it.
/// </remarks>
public sealed record ProductAdminDetailDto(
    long Id,
    string Title,
    string Slug,
    string? ShortDescription,
    string? Description,
    string Status,
    bool DeactivatedByAdmin,
    long BrandId,
    string BrandName,
    long PrimaryCategoryId,
    string PrimaryCategoryName,
    IReadOnlyList<long> SecondaryCategoryIds,
    string? Mpn,
    long? MergedIntoProductId,
    IReadOnlyList<long> SourceRefs,
    IReadOnlyList<ProductAdminVariantDto> Variants,
    IReadOnlyList<ProductAdminIdentifierDto> Identifiers,
    IReadOnlyList<ProductAdminMediaDto> Media,
    IReadOnlyList<string> ActivationProblems,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
