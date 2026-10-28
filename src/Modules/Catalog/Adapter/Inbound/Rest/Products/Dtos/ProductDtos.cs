namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Products.Dtos;

public sealed record CreateProductRequest(string Sku, string Name, decimal Price, string Currency, int InitialStock, string? Description);
public sealed record ProductResponse(long Id, string Sku, string Name, string? Description, decimal Price, string Currency, int Stock, int ReservedStock, int AvailableStock, bool IsActive);
public sealed record AdjustStockRequest(long ProductId, int Delta);
public sealed record ChangePriceRequest(long ProductId, decimal NewPrice, string Currency);
public sealed record ReserveStockRequest(int Quantity, int TtlMinutes);
