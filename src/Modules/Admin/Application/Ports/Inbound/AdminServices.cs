using EwaysVNext.Modules.Admin.Application.Views;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Admin.Application.Ports.Inbound;

/// <summary>One screen's worth of "how is the shop doing".</summary>
public interface IAdminDashboardService
{
    Task<AdminDashboard> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// Everything about one customer an operator needs while someone is on the phone.
/// </summary>
/// <remarks>
/// A composition, not a join: the profile comes from Customer and the orders from Sales, and
/// neither module knows the other exists. The alternative — a query spanning both — would put a
/// foreign key between two modules that are deliberately independent.
/// </remarks>
public interface ICustomer360Service
{
    Task<Result<Customer360>> GetAsync(
        long customerId, int recentOrderLimit = 20, CancellationToken ct = default);
}

public interface IOrder360Service
{
    Task<Result<Order360>> GetAsync(long orderId, CancellationToken ct = default);
}

public interface IAuditVerificationService
{
    Task<AuditVerificationView> VerifyAsync(CancellationToken ct = default);
}
