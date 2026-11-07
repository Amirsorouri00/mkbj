using EwaysVNext.Core.Application;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.QueryApplication.Partner;

/// <summary>
/// Everything a partner may read about its own orders.
/// </summary>
/// <remarks>
/// <para>
/// Every method takes the <see cref="PartnerPrincipal"/> and filters on it in the query, never
/// afterwards. A partner id is not bindable from a request anywhere in this system, and these
/// are the queries that would leak if it were.
/// </para>
/// <para>
/// Another partner's order is <b>NOT_FOUND</b>, never FORBIDDEN. A 403 confirms that the id
/// exists, which is exactly what someone probing for other partners' order numbers is after.
/// </para>
/// </remarks>
public interface IPartnerOrderQueries
{
    Task<Result<PartnerOrderDto>> GetAsync(
        PartnerPrincipal principal, long orderId, CancellationToken ct = default);

    Task<Result<PartnerOrderDto>> GetByExternalIdAsync(
        PartnerPrincipal principal, string externalOrderId, CancellationToken ct = default);

    Task<IReadOnlyList<PartnerOrderDto>> GetManyAsync(
        PartnerPrincipal principal, IReadOnlyCollection<long> orderIds, CancellationToken ct = default);

    Task<Result<CursorPage<PartnerOrderSummaryDto>>> ListAsync(
        PartnerPrincipal principal, PartnerOrderListQuery query, CancellationToken ct = default);

    Task<Result<CursorPage<PartnerOrderSummaryDto>>> ListForCustomerAsync(
        PartnerPrincipal principal, CustomerKey customer, long? cursor, int limit,
        CancellationToken ct = default);

    Task<Result<PartnerInvoiceDto>> GetInvoiceAsync(
        PartnerPrincipal principal, long documentId, CancellationToken ct = default);

    /// <summary>Invoices for up to a hundred of the partner's own order ids, in the order asked.</summary>
    Task<Result<IReadOnlyList<InvoiceSearchItem>>> SearchInvoicesAsync(
        PartnerPrincipal principal, IReadOnlyList<string> externalOrderIds,
        CancellationToken ct = default);
}
