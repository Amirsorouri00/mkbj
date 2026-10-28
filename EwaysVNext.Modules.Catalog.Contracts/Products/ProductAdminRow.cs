namespace EwaysVNext.Modules.Catalog.Contracts.Products;

public sealed record ProductAdminRow(
    long Id, string Sku, string Name, decimal Price, string Currency,
    int Stock, int ReservedStock, int AvailableStock, bool IsActive);
