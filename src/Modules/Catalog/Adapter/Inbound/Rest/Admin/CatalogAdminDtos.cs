using EwaysVNext.Modules.Catalog.Application.Products.Domain;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Admin;

public sealed record CreateProductRequest(
    long BrandId,
    long PrimaryCategoryId,
    string Title,
    string? Slug,
    string? ShortDescription,
    string? Description);

public sealed record UpdateProductRequest(
    long BrandId,
    long PrimaryCategoryId,
    string Title,
    string? ShortDescription,
    string? Description);

public sealed record SetSecondaryCategoriesRequest(IReadOnlyList<long> CategoryIds);

public sealed record AddVariantRequest(string? Sku, IReadOnlyDictionary<string, string>? Attributes);

public sealed record SetActiveRequest(bool IsActive);

public sealed record AddIdentifierRequest(
    ProductIdentifierType Type,
    string Value,
    string? SourceDomain);

/// <remarks><c>Status</c> is one of <c>none</c>, <c>activate</c>, <c>deactivate</c>.</remarks>
public sealed record BulkProductRequest(
    IReadOnlyList<long> ProductIds,
    string? Status,
    long? PrimaryCategoryId,
    long? BrandId);
