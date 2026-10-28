using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Contracts.Products;

public sealed record ProductSnapshot(SnowflakeId Id, string Name, decimal Price, string Currency, int AvailableStock);
