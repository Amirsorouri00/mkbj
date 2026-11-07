using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Services;

/// <summary>
/// Who the caller is, creating the record on first sight.
/// </summary>
/// <remarks>
/// Nothing in Keycloak tells this system that someone registered, so the first authenticated
/// request is the first moment a customer record can exist. A suspended account is refused here
/// rather than later: it must not be able to read its own history and then be surprised at
/// checkout.
/// </remarks>
internal sealed class ShopCustomerResolver(ISalesCustomerGateway customers) : IShopCustomerResolver
{
    public async Task<Result<long>> ResolveAsync(
        ShopCaller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var customer = await customers.EnsureWebsiteCustomerAsync(
            caller.IdentityUserId, caller.Email, caller.EmailVerified, ct);

        if (customer.IsFailure) return Result.Failure<long>(customer.Error!);

        return customer.Value!.IsActive
            ? Result.Success(customer.Value.CustomerId)
            : Result.Failure<long>(SalesErrors.CustomerSuspended);
    }
}
