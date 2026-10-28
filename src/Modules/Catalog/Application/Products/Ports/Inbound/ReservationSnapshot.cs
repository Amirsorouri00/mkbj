using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

public sealed record ReservationSnapshot(SnowflakeId ReservationId, SnowflakeId ProductId, int Quantity, DateTimeOffset ExpiresAt);
