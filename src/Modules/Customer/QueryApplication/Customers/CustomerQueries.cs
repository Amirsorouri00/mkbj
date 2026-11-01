using EwaysVNext.Database;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using Microsoft.EntityFrameworkCore;

// GreenDonut, which HotChocolate pulls in for data loaders, also ships a Result<T>. Aliasing the
// namespace keeps this file unambiguous wherever it is referenced from.
using Outcomes = EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.QueryApplication.Customers;

public interface ICustomerShopQueries
{
    Task<Outcomes.Result<CustomerProfileView>> GetProfileAsync(long customerId, CancellationToken ct = default);
}

public sealed class CustomerShopQueries(EwaysVNextDbContext db) : ICustomerShopQueries
{
    public async Task<Outcomes.Result<CustomerProfileView>> GetProfileAsync(
        long customerId, CancellationToken ct = default)
    {
        var profile = await db.Customers.AsNoTracking()
            .Where(customer => customer.Id == customerId)
            .Select(customer => new CustomerProfileView(
                customer.Id,
                customer.Email,
                customer.EmailVerified,
                customer.Phone,
                customer.FullName,
                customer.CompanyName,
                customer.BillingAddress == null
                    ? null
                    : new AddressSnapshot(
                        customer.BillingAddress.Line1,
                        customer.BillingAddress.Line2,
                        customer.BillingAddress.City,
                        customer.BillingAddress.PostalCode,
                        customer.BillingAddress.CountryIso2),
                customer.RegistrationNumber,
                customer.Status.ToString()))
            .FirstOrDefaultAsync(ct);

        return profile is null
            ? Outcomes.Result.Failure<CustomerProfileView>(Outcomes.Error.NotFound)
            : Outcomes.Result.Success(profile);
    }
}
