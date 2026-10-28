namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Dtos;

public sealed record CreateProductRequest(string Sku, string Name, decimal Price, string Currency, string? Description);
public sealed record ProductResponse(long Id, string Sku, string Name, string? Description, decimal Price, string Currency, bool IsActive);
public sealed record ChangePriceRequest(long ProductId, decimal NewPrice, string Currency);
