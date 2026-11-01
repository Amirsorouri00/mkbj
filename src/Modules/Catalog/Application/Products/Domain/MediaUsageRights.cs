namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

/// <summary>
/// Whether an image may be shown to customers.
/// <para>
/// A crawled image usually arrives as <see cref="RightsUnknown"/>, and that is deliberately not
/// the same as <see cref="Allowed"/>: a product cannot go active on an image nobody has confirmed
/// the seller may publish.
/// </para>
/// </summary>
public enum MediaUsageRights
{
    Allowed,
    RightsUnknown,
    Denied
}
