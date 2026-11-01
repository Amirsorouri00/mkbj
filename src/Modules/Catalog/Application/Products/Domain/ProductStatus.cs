namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

/// <summary>
/// Where a product sits between arriving and being sellable.
/// <para>
/// Stored by name rather than by number: these values appear in raw SQL in phase 3's storefront
/// views and in the check constraint, where a bare 2 says nothing to anyone reading it.
/// </para>
/// </summary>
public enum ProductStatus
{
    Draft,
    Active,
    Deactivated
}
