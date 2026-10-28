namespace EwaysVNext.Modules.Catalog.Contracts.Products;

public sealed record ProductAdminRow(
    long Id, string Sku, string Name, decimal Price, string Currency, bool IsActive);
