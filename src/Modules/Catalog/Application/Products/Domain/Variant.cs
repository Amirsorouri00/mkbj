using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

/// <summary>
/// One buyable form of a product — a size, a colour, or the only one there is.
/// <para>
/// The signature text and its hash are both stored. The hash is what the unique index and the
/// importer compare; the text is kept beside it so that a mismatch can be read by a human
/// without re-deriving it from the values.
/// </para>
/// </summary>
public sealed class Variant : Entity<long>
{
    public long ProductId { get; private set; }

    public Sku Sku { get; private set; } = null!;

    public string AttributeSignature { get; private set; } = null!;

    public string AttributeSignatureHash { get; private set; } = null!;

    public IReadOnlyDictionary<string, string> AttributeValues { get; private set; } =
        new Dictionary<string, string>();

    public bool IsActive { get; private set; }

    /// <summary>
    /// Set when an admin turned this variant off. An import may not undo it — see
    /// <see cref="Product.SetVariantActive"/>.
    /// </summary>
    public bool DeactivatedByAdmin { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    private Variant() { }

    internal Variant(long id, long productId, Sku sku, AttributeSignature signature, DateTimeOffset now)
    {
        Id = id;
        ProductId = productId;
        Sku = sku;
        AttributeSignature = signature.Text;
        AttributeSignatureHash = signature.Hash;
        AttributeValues = signature.Values;
        IsActive = true;
        CreatedAt = now;
    }

    internal bool SetActive(bool active, bool byAdmin, DateTimeOffset now)
    {
        // An import runs unattended and repeatedly; an admin switching a variant off is a
        // decision it must not quietly reverse on the next run.
        if (active && DeactivatedByAdmin && !byAdmin)
            return false;

        var changed = false;

        if (byAdmin && DeactivatedByAdmin == active)
        {
            DeactivatedByAdmin = !active;
            changed = true;
        }

        if (IsActive != active)
        {
            IsActive = active;
            changed = true;
        }

        if (changed) UpdatedAt = now;
        return true;
    }
}
