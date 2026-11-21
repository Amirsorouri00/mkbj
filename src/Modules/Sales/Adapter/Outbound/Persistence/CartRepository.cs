using Mkbj.Database;
using Mkbj.Modules.Sales.Application.Domain.Carts;
using Mkbj.Modules.Sales.Application.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace Mkbj.Modules.Sales.Adapter.Outbound.Persistence;

/// <summary>Carts, read and written through the one shared context.</summary>
internal sealed class CartRepository(MkbjDbContext db) : ICartRepository
{
    public Task<Cart?> GetByCustomerAsync(long customerId, CancellationToken ct = default) =>
        db.Carts.FirstOrDefaultAsync(cart => cart.CustomerId == customerId, ct);

    public Task AddAsync(Cart cart, CancellationToken ct = default)
    {
        db.Carts.Add(cart);
        return Task.CompletedTask;
    }
}
