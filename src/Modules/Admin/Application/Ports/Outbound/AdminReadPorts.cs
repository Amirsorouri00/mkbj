using EwaysVNext.Modules.Admin.Application.Views;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Admin.Application.Ports.Outbound;

/// <summary>
/// What Admin needs from Sales, in Admin's own terms.
/// </summary>
/// <remarks>
/// Read-only, and narrow on purpose. Admin composes; it does not change an order. Confirming a
/// payment or recording a refund stays on Sales' own admin surface, where the aggregate and its
/// audit live.
/// </remarks>
public interface ISalesReadPort
{
    Task<SalesSnapshot> GetDashboardAsync(DateTimeOffset now, CancellationToken ct = default);

    Task<IReadOnlyList<OrderRow>> ListCustomerOrdersAsync(
        long customerId, int limit, CancellationToken ct = default);

    Task<Result<OrderSheet>> GetOrderAsync(long orderId, CancellationToken ct = default);
}

/// <remarks>
/// The window is passed in rather than chosen here, so the dashboard decides what "recently"
/// means in one place instead of each provider having an opinion.
/// </remarks>
public interface ISourcingReadPort
{
    Task<ImportSnapshot> GetDashboardCountsAsync(
        DateTimeOffset since, CancellationToken ct = default);
}

public interface ICustomerReadPort
{
    Task<Result<CustomerProfile>> GetAsync(long customerId, CancellationToken ct = default);
}

/// <summary>Whether the audit chain still adds up.</summary>
public interface IAuditReadPort
{
    Task<AuditVerificationView> VerifyAsync(DateTimeOffset now, CancellationToken ct = default);
}
