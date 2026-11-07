using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Admin.Application.Ports.Inbound;
using EwaysVNext.Modules.Admin.Application.Ports.Outbound;
using EwaysVNext.Modules.Admin.Application.Views;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Admin.Application.Services;

/// <summary>
/// The dashboard, composed from what each module already knows.
/// </summary>
/// <remarks>
/// <para>
/// Admin computes one thing of its own — the failure codes rolled up by count — and copies the
/// rest. The roll-up belongs here rather than in Sales because it is a <b>presentation</b>
/// question: Sales records every failure individually, which is what telemetry is for, and the
/// dashboard asks which kinds are happening.
/// </para>
/// <para>
/// There is no import section yet. The Sourcing module arrives in phase 7, and a section of
/// zeros would read as "nothing is importing" rather than "nothing imports yet".
/// </para>
/// </remarks>
internal sealed class AdminDashboardService(ISalesReadPort sales, IClock clock)
    : IAdminDashboardService
{
    public async Task<AdminDashboard> GetAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var snapshot = await sales.GetDashboardAsync(now, ct);

        var codes = snapshot.RecentFailures
            .GroupBy(failure => failure.ErrorCode, StringComparer.Ordinal)
            .Select(group => new FailureCodeCount(group.Key, group.Count()))
            // Most frequent first, then by name so equal counts have a stable order rather than
            // one that shuffles between refreshes.
            .OrderByDescending(code => code.Count)
            .ThenBy(code => code.ErrorCode, StringComparer.Ordinal)
            .ToList();

        return new AdminDashboard(now, snapshot, codes);
    }
}

/// <summary>
/// One customer, with what they have bought.
/// </summary>
/// <remarks>
/// The customer is fetched first and its failure is returned as it stands: there is no useful
/// 360 of a customer who does not exist, and asking Sales for the orders of a missing customer
/// would be work thrown away.
/// </remarks>
internal sealed class Customer360Service(ICustomerReadPort customers, ISalesReadPort sales)
    : ICustomer360Service
{
    public async Task<Result<Customer360>> GetAsync(
        long customerId, int recentOrderLimit = 20, CancellationToken ct = default)
    {
        var customer = await customers.GetAsync(customerId, ct);
        if (customer.IsFailure) return Result.Failure<Customer360>(customer.Error!);

        var orders = await sales.ListCustomerOrdersAsync(customerId, recentOrderLimit, ct);

        return Result.Success(new Customer360(customer.Value!, orders));
    }
}

/// <summary>
/// One order, with who bought it.
/// </summary>
/// <remarks>
/// A missing customer does <b>not</b> fail the request. The order is a real sale with real
/// documents, and refusing to show it because the buyer's record has since gone would hide the
/// evidence exactly when someone is looking for it.
/// </remarks>
internal sealed class Order360Service(ISalesReadPort sales, ICustomerReadPort customers)
    : IOrder360Service
{
    public async Task<Result<Order360>> GetAsync(long orderId, CancellationToken ct = default)
    {
        var order = await sales.GetOrderAsync(orderId, ct);
        if (order.IsFailure) return Result.Failure<Order360>(order.Error!);

        var customer = await customers.GetAsync(order.Value!.Summary.CustomerId, ct);

        return Result.Success(new Order360(
            order.Value, customer.IsSuccess ? customer.Value : null));
    }
}

internal sealed class AuditVerificationService(IAuditReadPort audit, IClock clock)
    : IAuditVerificationService
{
    public Task<AuditVerificationView> VerifyAsync(CancellationToken ct = default) =>
        audit.VerifyAsync(clock.UtcNow, ct);
}
