using EwaysVNext.Database;
using EwaysVNext.Modules.Sales.Application.Domain.Carts;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound.Persistence;

/// <summary>Carts, read and written through the one shared context.</summary>
internal sealed class CartRepository(EwaysVNextDbContext db) : ICartRepository
{
    public Task<Cart?> GetByCustomerAsync(long customerId, CancellationToken ct = default) =>
        db.Carts.FirstOrDefaultAsync(cart => cart.CustomerId == customerId, ct);

    public Task AddAsync(Cart cart, CancellationToken ct = default)
    {
        db.Carts.Add(cart);
        return Task.CompletedTask;
    }
}
