using EwaysVNext.Database;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Queries.Application.Paginations;
using Microsoft.EntityFrameworkCore;

using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer;
using Outcomes = EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.QueryApplication.Customers;

public interface ICustomerAdminQueries
{
    Task<PaginatedQuery<CustomerAdminListItem>> ListAsync(
        AdminCustomerListFilter filter, CancellationToken ct = default);

    Task<Outcomes.Result<CustomerAdminDetail>> GetAsync(long customerId, CancellationToken ct = default);
}

public sealed class CustomerAdminQueries(EwaysVNextDbContext db) : ICustomerAdminQueries
{
    public const int MaxPageSize = 100;

    public async Task<PaginatedQuery<CustomerAdminListItem>> ListAsync(
        AdminCustomerListFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var query = db.Customers.AsNoTracking();

        if (Enum.TryParse<CustomerKind>(filter.Kind, ignoreCase: true, out var kind))
            query = query.Where(customer => customer.Kind == kind);

        if (Enum.TryParse<CustomerStatus>(filter.Status, ignoreCase: true, out var status))
            query = query.Where(customer => customer.Status == status);

        if (filter.PartnerId is { } partnerId)
            query = query.Where(customer => customer.PartnerId == partnerId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // ILIKE with both wildcards: an admin has a fragment, not a prefix — the customer on
            // the phone reads out the last four digits of a number as often as the first.
            var pattern = $"%{filter.Search.Trim()}%";

            query = query.Where(customer =>
                (customer.Email != null && EF.Functions.ILike(customer.Email, pattern))
                || (customer.Phone != null && EF.Functions.ILike(customer.Phone, pattern))
                || (customer.FullName != null && EF.Functions.ILike(customer.FullName, pattern))
                || (customer.CompanyName != null && EF.Functions.ILike(customer.CompanyName, pattern))
                || (customer.ExternalCustomerId != null && EF.Functions.ILike(customer.ExternalCustomerId, pattern)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(customer => customer.CreatedAt)
            .ThenByDescending(customer => customer.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(customer => ListItem(customer))
            .ToListAsync(ct);

        return new PaginatedQuery<CustomerAdminListItem>(
            page, pageSize, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize), total, items);
    }

    public async Task<Outcomes.Result<CustomerAdminDetail>> GetAsync(
        long customerId, CancellationToken ct = default)
    {
        var detail = await db.Customers.AsNoTracking()
            .Where(customer => customer.Id == customerId)
            .Select(customer => new CustomerAdminDetail(
                ListItem(customer),
                customer.EmailVerified,
                customer.BillingAddress == null
                    ? null
                    : new AddressSnapshot(
                        customer.BillingAddress.Line1,
                        customer.BillingAddress.Line2,
                        customer.BillingAddress.City,
                        customer.BillingAddress.PostalCode,
                        customer.BillingAddress.CountryIso2),
                customer.RegistrationNumber,
                customer.IdentityUserId,
                customer.ContactChanges
                    .OrderByDescending(change => change.ChangedAt)
                    .ThenByDescending(change => change.Id)
                    .Select(change => new ContactChangeView(
                        change.Field.ToString(),
                        change.OldValue,
                        change.NewValue,
                        change.ChangedAt,
                        change.SourcePartnerId,
                        change.CorrelationId))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return detail is null
            ? Outcomes.Result.Failure<CustomerAdminDetail>(Outcomes.Error.NotFound)
            : Outcomes.Result.Success(detail);
    }

    /// <summary>
    /// Shared between list and detail so the two cannot disagree about what a customer looks
    /// like. Written as an expression the provider can translate, not a method it would have to
    /// evaluate in memory.
    /// </summary>
    private static CustomerAdminListItem ListItem(CustomerAggregate customer) =>
        new(
            customer.Id,
            customer.Kind.ToString(),
            customer.PartnerId,
            customer.ExternalCustomerId,
            customer.Email,
            customer.Phone,
            customer.FullName,
            customer.CompanyName,
            customer.Status.ToString(),
            customer.CreatedAt);
}
