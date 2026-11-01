using System.Globalization;

using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Customer.Application.Customers.Services;

public sealed class CustomerAdminService(
    ICustomerRepository repository,
    IAuditTrail audit,
    IUnitOfWork unitOfWork) : ICustomerAdminService
{
    public Task<Result> SuspendAsync(long customerId, AdminActor actor, CancellationToken ct = default) =>
        SetStatusAsync(customerId, actor, CustomerStatus.Suspended, "customer.suspended", ct);

    public Task<Result> ReactivateAsync(long customerId, AdminActor actor, CancellationToken ct = default) =>
        SetStatusAsync(customerId, actor, CustomerStatus.Active, "customer.reactivated", ct);

    private async Task<Result> SetStatusAsync(
        long customerId, AdminActor actor, CustomerStatus target, string action, CancellationToken ct)
    {
        var customer = await repository.GetAsync(customerId, ct);

        if (customer is null)
            return Result.Failure(Error.NotFound);

        var before = customer.Status;

        if (target == CustomerStatus.Suspended)
            customer.Suspend();
        else
            customer.Reactivate();

        // Recorded even when the status did not move. "Who tried to suspend this customer, and
        // when" is the question an audit answers, and a no-op suspend is still an instruction
        // someone gave.
        audit.Record(actor.ToAudit(), action, nameof(Customer),
            customerId.ToString(CultureInfo.InvariantCulture),
            before: new { Status = before.ToString() },
            after: new { Status = customer.Status.ToString() });

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
