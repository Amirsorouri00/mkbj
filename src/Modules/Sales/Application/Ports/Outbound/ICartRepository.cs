using EwaysVNext.Modules.Sales.Application.Domain.Carts;

namespace EwaysVNext.Modules.Sales.Application.Ports.Outbound;

/// <summary>One cart per customer, loaded with its lines.</summary>
public interface ICartRepository
{
    Task<Cart?> GetByCustomerAsync(long customerId, CancellationToken ct = default);

    Task AddAsync(Cart cart, CancellationToken ct = default);
}
