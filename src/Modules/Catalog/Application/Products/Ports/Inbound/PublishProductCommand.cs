using EwaysVNext.Modules.Catalog.Application.Products.Domain;

namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

public sealed record PublishBrand(string Name, IReadOnlyList<string> Aliases);

/// <remarks>
/// The image is named by where it already sits in staging, not by its bytes: an import has
/// downloaded it once already, and pulling it back through this process to push it out again
/// would double the traffic for nothing.
/// </remarks>
public sealed record PublishMedia(
    string StagingObjectKey,
    string? Alt,
    string? SourceUrl,
    MediaUsageRights Rights,
    int SortOrder);

/// <remarks>A null <c>Sku</c> takes the derived form, as it does everywhere else.</remarks>
public sealed record PublishVariant(IReadOnlyDictionary<string, string> Attributes, string? Sku);

/// <summary>
/// One product as a source says it is, to be made true in the catalogue.
/// </summary>
/// <remarks>
/// <para>
/// <c>CategoryKeyPath</c> is root to leaf, because a canonical key is only unique within its
/// parent: "Kettles" under Kitchen and "Kettles" under Outdoors are two categories, and a feed
/// that says only "Kettles" has not said which.
/// </para>
/// <para>
/// <c>TargetProductId</c> is the admin's "this listing is really that product" override. It
/// skips identifier matching entirely — the admin has already answered the question matching
/// exists to answer, and guessing differently would overrule them.
/// </para>
/// </remarks>
public sealed record PublishProductCommand(
    PublishBrand Brand,
    IReadOnlyList<string> CategoryKeyPath,
    string Title,
    string? ShortDescription,
    string? Description,
    IReadOnlyList<IdentifierInput> Identifiers,
    IReadOnlyList<PublishMedia> Media,
    IReadOnlyList<PublishVariant> Variants,
    IReadOnlyList<long> SourceListingIds,
    string Provenance,
    long? TargetProductId = null);

public sealed record PublishedVariant(long VariantId, string Sku, string AttributeSignature, bool Created);

/// <remarks>
/// <c>SkippedBecauseDeactivated</c> is true when the product was updated but left off, because an
/// admin had switched it off. The import did its work; the decision to keep it off sale stands.
/// </remarks>
public sealed record PublishedProduct(
    long ProductId,
    bool Created,
    bool SkippedBecauseDeactivated,
    IReadOnlyList<PublishedVariant> Variants);
