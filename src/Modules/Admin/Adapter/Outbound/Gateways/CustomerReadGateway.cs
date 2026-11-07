using EwaysVNext.Modules.Admin.Application.Ports.Outbound;
using EwaysVNext.Modules.Admin.Application.Views;
using EwaysVNext.Modules.Customer.QueryApplication.Customers;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Admin.Adapter.Outbound.Gateways;

/// <summary>Customer's admin read side, mapped into Admin's own profile view.</summary>
internal sealed class CustomerReadGateway(ICustomerAdminQueries customers) : ICustomerReadPort
{
    public async Task<Result<CustomerProfile>> GetAsync(
        long customerId, CancellationToken ct = default)
    {
        var found = await customers.GetAsync(customerId, ct);

        if (found.IsFailure) return Result.Failure<CustomerProfile>(found.Error!);

        var detail = found.Value!;
        var customer = detail.Customer;

        return Result.Success(new CustomerProfile(
            customer.Id,
            customer.Kind,
            customer.PartnerId,
            customer.ExternalCustomerId,
            customer.Email,
            customer.Phone,
            customer.FullName,
            customer.CompanyName,
            customer.Status,
            detail.EmailVerified,
            detail.BillingAddress is { } address
                ? new AddressView(
                    address.Line1, address.Line2, address.City, address.PostalCode,
                    address.CountryIso2)
                : null,
            detail.RegistrationNumber,
            customer.CreatedAt,
            // Newest first: an operator looking at contact history is asking what changed most
            // recently, not what the record looked like when it was created.
            [.. detail.ContactHistory
                .OrderByDescending(change => change.ChangedAt)
                .Select(change => new ContactChangeEntry(
                    change.Field,
                    change.OldValue,
                    change.NewValue,
                    change.ChangedAt,
                    change.SourcePartnerId,
                    change.CorrelationId))]));
    }
}
