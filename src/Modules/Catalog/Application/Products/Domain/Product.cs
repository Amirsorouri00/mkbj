using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Common;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

/// <summary>
/// A product and everything that makes it sellable: its variants, its images and the codes it is
/// known by elsewhere.
/// <para>
/// Two things write to this aggregate — an admin, and an unattended import that runs again and
/// again over the same feed. Every method here is shaped by that: repeating an import must be a
/// no-op, and an import must never undo a decision an admin made. That is what
/// <see cref="DeactivatedByAdmin"/> is for, and why the activation methods take a
/// <c>byAdmin</c> flag rather than reading one from ambient context.
/// </para>
/// </summary>
public sealed class Product : AggregateRoot<long>
{
    public const int TitleMaxLength = 200;
    public const int ShortDescriptionMaxLength = 500;
    public const int DescriptionMaxLength = 20_000;
    public const int SlugMaxLength = 240;

    private readonly List<Variant> _variants = [];
    private readonly List<ProductMedia> _media = [];
    private readonly List<ProductIdentifier> _identifiers = [];
    private readonly List<long> _sourceRefs = [];

    public long BrandId { get; private set; }

    public long PrimaryCategoryId { get; private set; }

    public string Title { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public string? ShortDescription { get; private set; }

    public string? Description { get; private set; }

    public ProductStatus Status { get; private set; }

    /// <summary>
    /// True when the product is off because an admin said so, as opposed to because an import
    /// found nothing sellable. Only an admin can clear it.
    /// </summary>
    public bool DeactivatedByAdmin { get; private set; }

    /// <summary>
    /// The manufacturer part number, duplicated out of <see cref="Identifiers"/> so that
    /// "this brand's part number" can be a unique index rather than a scan of a child table.
    /// </summary>
    public string? Mpn { get; private set; }

    /// <summary>Set when this product turned out to be a duplicate of another.</summary>
    public long? MergedIntoProductId { get; private set; }

    /// <summary>The source listings this product was built from, for tracing an import back.</summary>
    public IReadOnlyList<long> SourceRefs => _sourceRefs;

    public IReadOnlyList<Variant> Variants => _variants;

    public IReadOnlyList<ProductMedia> Media => _media;

    public IReadOnlyList<ProductIdentifier> Identifiers => _identifiers;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    private Product() { }

    public static Product CreateDraft(long id, long brandId, long primaryCategoryId, string title,
        string? slug, string? shortDescription, string? description, DateTimeOffset now)
    {
        if (id <= 0)
        {
            throw new DomainException("PRODUCT_ID_INVALID", "Product ID must be positive.");
        }

        var normalizedTitle = NormalizeTitle(title);

        return new Product
        {
            Id = id,
            BrandId = brandId,
            PrimaryCategoryId = primaryCategoryId,
            Title = normalizedTitle,
            // The id is part of the default slug because two products legitimately share a title
            // ("Black T-Shirt"), and the slug is globally unique.
            Slug = NormalizeSlug(slug) ?? $"{CanonicalKey.From(normalizedTitle)}-{id}",
            ShortDescription = NormalizeOptional(shortDescription, ShortDescriptionMaxLength, "PRODUCT_SHORT_DESCRIPTION_TOO_LONG"),
            Description = NormalizeOptional(description, DescriptionMaxLength, "PRODUCT_DESCRIPTION_TOO_LONG"),
            Status = ProductStatus.Draft,
            CreatedAt = now
        };
    }

    public void UpdateDetails(long brandId, long primaryCategoryId, string title,
        string? shortDescription, string? description, DateTimeOffset now)
    {
        EnsureNotMerged();

        BrandId = brandId;
        PrimaryCategoryId = primaryCategoryId;
        Title = NormalizeTitle(title);
        ShortDescription = NormalizeOptional(shortDescription, ShortDescriptionMaxLength, "PRODUCT_SHORT_DESCRIPTION_TOO_LONG");
        Description = NormalizeOptional(description, DescriptionMaxLength, "PRODUCT_DESCRIPTION_TOO_LONG");

        // The slug is deliberately left alone: it is a URL customers may already have bookmarked
        // and search engines have indexed, so renaming a product does not move it.
        UpdatedAt = now;
    }

    /// <summary>
    /// Records a code this product is known by. Adding the same code again is a no-op, because an
    /// import re-reads the same feed row every run.
    /// </summary>
    public void AddIdentifier(long identifierId, ProductIdentifierType type, string value,
        string? sourceDomain, DateTimeOffset now)
    {
        EnsureNotMerged();

        var normalized = ProductIdentifier.Normalize(type, value, sourceDomain);

        if (_identifiers.Any(identifier => identifier.Type == type &&
                string.Equals(identifier.Value, normalized, StringComparison.Ordinal)))
            return;

        if (type == ProductIdentifierType.Mpn && Mpn is not null)
        {
            throw new DomainException(
                "PRODUCT_MPN_ALREADY_SET",
                $"This product already has the part number '{Mpn}'; a product has one MPN per brand.");
        }

        _identifiers.Add(ProductIdentifier.Create(identifierId, Id, type, value, sourceDomain));

        if (type == ProductIdentifierType.Mpn)
        {
            Mpn = normalized;
        }

        UpdatedAt = now;
    }

    public void RemoveIdentifier(long identifierId, DateTimeOffset now)
    {
        EnsureNotMerged();

        var identifier = _identifiers.FirstOrDefault(candidate => candidate.Id == identifierId)
            ?? throw new DomainException(
                "PRODUCT_IDENTIFIER_NOT_FOUND", "The identifier was not found on this product.");

        _identifiers.Remove(identifier);

        if (identifier.Type == ProductIdentifierType.Mpn)
        {
            Mpn = null;
        }

        UpdatedAt = now;
    }

    public void AddSourceRef(long sourceListingId, DateTimeOffset now)
    {
        if (sourceListingId <= 0)
        {
            throw new DomainException("PRODUCT_SOURCE_REF_INVALID", "Source listing ID must be positive.");
        }

        if (_sourceRefs.Contains(sourceListingId))
            return;

        _sourceRefs.Add(sourceListingId);
        UpdatedAt = now;
    }

    public Variant AddVariant(long variantId, string sku, IReadOnlyDictionary<string, string> attributes,
        DateTimeOffset now)
    {
        EnsureNotMerged();

        if (variantId <= 0)
        {
            throw new DomainException("VARIANT_ID_INVALID", "Variant ID must be positive.");
        }

        var signature = Domain.AttributeSignature.From(attributes);
        var parsedSku = Sku.Create(sku);

        if (_variants.Any(variant => variant.Sku.Equals(parsedSku)))
        {
            throw new DomainException(
                "VARIANT_SKU_DUPLICATE", $"SKU '{parsedSku.Value}' is already used by another variant.");
        }

        // A variant with no attributes says "this product has only one form". Mixing it with
        // attributed variants leaves nothing to tell a customer which one they are buying.
        var hasUnattributed = _variants.Any(variant => variant.AttributeSignature.Length == 0);
        if (_variants.Count > 0 && (hasUnattributed || signature.Text.Length == 0))
        {
            throw new DomainException(
                "VARIANT_SIGNATURE_CONFLICT",
                "A variant with no attributes must be the product's only variant.");
        }

        if (_variants.Any(variant =>
                string.Equals(variant.AttributeSignatureHash, signature.Hash, StringComparison.Ordinal)))
        {
            throw new DomainException(
                "VARIANT_SIGNATURE_DUPLICATE",
                $"Another variant of this product already has the attributes '{signature.Text}'.");
        }

        var created = new Variant(variantId, Id, parsedSku, signature, now);
        _variants.Add(created);
        UpdatedAt = now;
        return created;
    }

    /// <summary>
    /// Turns a variant on or off. Returns false when an import tried to re-enable something an
    /// admin disabled — a refusal, not a failure, so a feed full of them still completes.
    /// </summary>
    public bool SetVariantActive(long variantId, bool active, bool byAdmin, DateTimeOffset now)
    {
        var variant = _variants.FirstOrDefault(candidate => candidate.Id == variantId)
            ?? throw new DomainException("VARIANT_NOT_FOUND", "The variant was not found on this product.");

        if (!variant.SetActive(active, byAdmin, now))
            return false;

        UpdatedAt = now;
        Raise(new VariantActivationChangedDomainEvent(Id, variantId, variant.IsActive));

        // Nothing is left to sell, so the product follows — with the same origin, or an admin
        // turning off the last variant would leave a product an import could switch back on.
        if (!active && Status == ProductStatus.Active && !_variants.Any(candidate => candidate.IsActive))
        {
            Deactivate(byAdmin, now);
        }

        return true;
    }

    public ProductMedia AddMedia(long mediaId, string objectKey, int sortOrder, string? alt,
        string? sourceUrl, MediaUsageRights rights, DateTimeOffset now)
    {
        EnsureNotMerged();

        if (mediaId <= 0)
        {
            throw new DomainException("PRODUCT_MEDIA_ID_INVALID", "Media ID must be positive.");
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new DomainException("PRODUCT_MEDIA_KEY_REQUIRED", "Media object key is required.");
        }

        if (!Enum.IsDefined(rights))
        {
            throw new DomainException("PRODUCT_MEDIA_RIGHTS_INVALID", "Media usage rights are invalid.");
        }

        var created = new ProductMedia(
            mediaId, Id, objectKey.Trim(), sortOrder, alt?.Trim(), sourceUrl?.Trim(), rights, now);
        _media.Add(created);
        UpdatedAt = now;
        return created;
    }

    public ProductMedia RemoveMedia(long mediaId, DateTimeOffset now)
    {
        var media = _media.FirstOrDefault(candidate => candidate.Id == mediaId)
            ?? throw new DomainException(
                "PRODUCT_MEDIA_NOT_FOUND", "The image was not found on this product.");

        _media.Remove(media);
        UpdatedAt = now;
        return media;
    }

    /// <summary>
    /// Everything standing between this product and being sellable, as stable codes. Empty means
    /// <see cref="Activate"/> will succeed.
    /// </summary>
    public IReadOnlyList<string> ActivationProblems()
    {
        var problems = new List<string>();

        if (!_variants.Any(variant => variant.IsActive)) problems.Add("NO_ACTIVE_VARIANT");
        if (BrandId <= 0) problems.Add("NO_BRAND");
        if (PrimaryCategoryId <= 0) problems.Add("NO_PRIMARY_CATEGORY");
        if (string.IsNullOrWhiteSpace(Title)) problems.Add("NO_TITLE");
        if (!_media.Any(media => media.IsUsable)) problems.Add("NO_USABLE_IMAGE");

        return problems;
    }

    /// <summary>
    /// Makes the product sellable. Returns false when an import tried to re-enable a product an
    /// admin switched off.
    /// </summary>
    public bool Activate(bool byAdmin, DateTimeOffset now)
    {
        EnsureNotMerged();

        if (DeactivatedByAdmin && !byAdmin)
            return false;

        if (Status == ProductStatus.Active)
            return true;

        var problems = ActivationProblems();
        if (problems.Count > 0)
        {
            throw new DomainException(
                "PRODUCT_ACTIVATION_INVALID", string.Join(',', problems));
        }

        Status = ProductStatus.Active;
        DeactivatedByAdmin = false;
        UpdatedAt = now;
        Raise(new ProductActivatedDomainEvent(Id));
        return true;
    }

    public void Deactivate(bool byAdmin, DateTimeOffset now)
    {
        var changed = false;

        // An admin deactivating an already-off product still matters: it pins the product off
        // against the next import run, which is the whole point of the flag.
        if (byAdmin && !DeactivatedByAdmin)
        {
            DeactivatedByAdmin = true;
            changed = true;
        }

        if (Status != ProductStatus.Deactivated)
        {
            Status = ProductStatus.Deactivated;
            changed = true;
            Raise(new ProductDeactivatedDomainEvent(Id, byAdmin));
        }

        if (changed) UpdatedAt = now;
    }

    /// <summary>
    /// Records that this product is a duplicate of <paramref name="survivorId"/>. It is
    /// deactivated as an admin decision, because a merge is one and an import must not revive it.
    /// </summary>
    public void MarkMergedInto(long survivorId, DateTimeOffset now)
    {
        if (survivorId <= 0 || survivorId == Id || MergedIntoProductId.HasValue)
        {
            throw new DomainException(
                "PRODUCT_MERGE_INVALID",
                "A product can be merged once, into a different product.");
        }

        MergedIntoProductId = survivorId;
        Deactivate(byAdmin: true, now);
    }

    private void EnsureNotMerged()
    {
        if (MergedIntoProductId.HasValue)
        {
            throw new DomainException(
                "PRODUCT_MERGED",
                "This product was merged into another; edit the surviving product instead.");
        }
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("PRODUCT_TITLE_REQUIRED", "Product title is required.");
        }

        var normalized = title.Trim();
        if (normalized.Length > TitleMaxLength)
        {
            throw new DomainException(
                "PRODUCT_TITLE_TOO_LONG", $"Product title cannot exceed {TitleMaxLength} characters.");
        }

        return normalized;
    }

    private static string? NormalizeSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;

        // No length check: CanonicalKey caps at 120, and the default form adds only the id, so
        // the 240-character column cannot be overrun from here.
        return CanonicalKey.From(slug);
    }

    private static string? NormalizeOptional(string? value, int maxLength, string tooLongCode)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainException(tooLongCode, $"This text cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}
