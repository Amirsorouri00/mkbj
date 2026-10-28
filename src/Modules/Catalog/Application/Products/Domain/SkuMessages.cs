namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

internal static class SkuMessages
{
    public const string Empty = "SKU cannot be empty.";
    public const string Length = "SKU must be between 3 and 32 characters.";
    public const string Format = "SKU may only contain letters, digits and hyphens.";
}
