using EwaysVNext.Database;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer;

namespace EwaysVNext.Modules.Customer.Adapter.Outbound.Persistence;

/// <summary>
/// Loads customers with their contact history. Tracked: every caller either mutates the
/// aggregate or is about to decide whether to.
/// </summary>
public sealed class CustomerRepository(EwaysVNextDbContext db) : ICustomerRepository
{
    public Task<CustomerAggregate?> GetAsync(long id, CancellationToken ct = default) =>
        db.Customers
            .Include(customer => customer.ContactChanges)
            .SingleOrDefaultAsync(customer => customer.Id == id, ct);

    public Task<CustomerAggregate?> FindPartnerLinkedAsync(
        long partnerId, string externalCustomerId, CancellationToken ct = default) =>
        db.Customers
            .Include(customer => customer.ContactChanges)
            .SingleOrDefaultAsync(
                customer => customer.Kind == CustomerKind.PartnerLinked
                    && customer.PartnerId == partnerId
                    && customer.ExternalCustomerId == externalCustomerId,
                ct);

    public Task<CustomerAggregate?> FindWebsiteAsync(Guid identityUserId, CancellationToken ct = default) =>
        db.Customers
            .Include(customer => customer.ContactChanges)
            .SingleOrDefaultAsync(
                customer => customer.Kind == CustomerKind.Website
                    && customer.IdentityUserId == identityUserId,
                ct);

    public void Add(CustomerAggregate customer) => db.Customers.Add(customer);
}
