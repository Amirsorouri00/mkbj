using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

public sealed class ProductReservation : Entity<long>
{
    public long ProductId { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsCommitted { get; private set; }

    public DateTimeOffset? CommittedAt { get; private set; }

    private ProductReservation() { }

    internal ProductReservation(long id, long productId, int quantity, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id; ProductId = productId; Quantity = quantity; CreatedAt = createdAt; ExpiresAt = expiresAt;
    }

    internal void Commit(DateTimeOffset committedAt) { IsCommitted = true; CommittedAt = committedAt; }
    internal void Revert() { IsCommitted = false; CommittedAt = null; }
}
