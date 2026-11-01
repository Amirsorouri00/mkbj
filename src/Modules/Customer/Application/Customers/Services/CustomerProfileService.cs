using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Customer.Application.Customers.Services;

public sealed class CustomerProfileService(
    ICustomerRepository repository,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : ICustomerProfileService
{
    public async Task<Result> UpdateProfileAsync(
        long customerId, UpdateProfileCommand command, string correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customer = await repository.GetAsync(customerId, ct);

        if (customer is null)
            return Result.Failure(Error.NotFound);

        Phone? phone = null;
        if (command.Phone is not null && !Phone.TryCreate(command.Phone, out phone))
        {
            return Result.Failure(new Error("VALIDATION_FAILED",
                $"'{command.Phone}' is not a valid phone number. Use E.164, for example '+6591234567'."));
        }

        Address? address;
        try
        {
            address = command.BillingAddress is null
                ? null
                : Address.Create(
                    command.BillingAddress.Line1,
                    command.BillingAddress.Line2,
                    command.BillingAddress.City,
                    command.BillingAddress.PostalCode,
                    command.BillingAddress.CountryIso2);

            customer.UpdateProfile(
                new ProfileUpdate(
                    command.FullName, command.CompanyName, phone, address, command.RegistrationNumber),
                correlationId, ids.Generate, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(ex.Code, ex.Message));
        }

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
