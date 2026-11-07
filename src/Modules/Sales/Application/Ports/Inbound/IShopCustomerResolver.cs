using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Ports.Inbound;

/// <summary>
/// Turns the caller's token into the customer id the read side filters on.
/// </summary>
/// <remarks>
/// An inbound port rather than letting an adapter call the customer gateway: the gateway is an
/// <b>outbound</b> port, and an adapter reaching across to it would make the hexagon a circle.
/// It also means one answer to "who is this?" for the cart, checkout and order history alike.
/// </remarks>
public interface IShopCustomerResolver
{
    Task<Result<long>> ResolveAsync(ShopCaller caller, CancellationToken ct = default);
}
