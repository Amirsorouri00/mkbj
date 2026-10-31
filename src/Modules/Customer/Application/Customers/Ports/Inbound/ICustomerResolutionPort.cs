using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;

/// <summary>
/// How Sales turns a partner's customer reference into a customer of ours.
/// <para>
/// Finding and creating are separate calls, and creating does not sync. A partner placing an
/// order sends contact details every time; if create silently also synced, the first order for an
/// existing customer would overwrite details without the caller having asked. Sales decides when
/// to sync (spec §10.3).
/// </para>
/// </summary>
public interface ICustomerResolutionPort
{
    Task<PartnerCustomerRef?> FindPartnerCustomerAsync(
        long partnerId, string externalCustomerId, CancellationToken ct = default);

    Task<Result<PartnerCustomerRef>> CreatePartnerCustomerAsync(
        long partnerId, string externalCustomerId, ContactInput contact, string correlationId,
        CancellationToken ct = default);

    Task<Result> SyncPartnerContactAsync(
        long customerId, long partnerId, ContactInput contact, string correlationId,
        CancellationToken ct = default);

    Task<Result<InvoiceParty>> GetInvoicePartyAsync(long customerId, CancellationToken ct = default);
}
