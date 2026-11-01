using EwaysVNext.Core.Application;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;

/// <summary>
/// Suspending a customer is day-to-day shop work — a chargeback, a fraud flag, a request from the
/// customer themselves — so staff may do it. It is reversible and does not touch their details;
/// what it stops is the customer acting, not the seller's record of them.
/// </summary>
public interface ICustomerAdminService
{
    Task<Result> SuspendAsync(long customerId, AdminActor actor, CancellationToken ct = default);

    Task<Result> ReactivateAsync(long customerId, AdminActor actor, CancellationToken ct = default);
}
