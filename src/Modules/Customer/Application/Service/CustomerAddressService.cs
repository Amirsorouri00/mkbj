using System.Text.RegularExpressions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Modules.Location.Contracts.Abstractions;
using EwaysVNext.Modules.Location.Contracts.Locations;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;

namespace EwaysVNext.Modules.Customer.Application.Service;


public sealed class CustomerAddressService(
    ICustomerRepository customers,
    ILocationService locations,
    IUnitOfWork unitOfWork,
    IIdGenerator idGenerator) : ICustomerAddressInboundPort
{
    private static readonly Regex PostalCodeRegex =
        new("^[0-9]{10}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const int AddressTextMaxLength = 1000;
    private const int RecipientNameMaxLength = 200;

    private static readonly Error CustomerNotFound = new("CUSTOMER_NOT_FOUND", CustomerAddressServiceMessages.CustomerNotFound);
    private static readonly Error AddressNotFound = new("ADDRESS_NOT_FOUND", CustomerAddressServiceMessages.AddressNotFound);
    private static readonly Error InvalidPostalCode =
        new("INVALID_POSTAL_CODE", CustomerAddressServiceMessages.InvalidPostalCode);
    private static readonly Error InvalidCity = new("INVALID_CITY", CustomerAddressServiceMessages.InvalidCity);
    private static readonly Error InvalidDistrict =
        new("INVALID_DISTRICT", CustomerAddressServiceMessages.InvalidDistrict);
    private static readonly Error AddressTextTooLong =
        new("ADDRESS_TEXT_TOO_LONG", CustomerAddressServiceMessages.AddressTextTooLong);
    private static readonly Error RecipientNameRequired =
        new("RECIPIENT_NAME_REQUIRED", CustomerAddressServiceMessages.RecipientNameRequired);
    private static readonly Error RecipientNameTooLong =
        new("RECIPIENT_NAME_TOO_LONG", CustomerAddressServiceMessages.RecipientNameTooLong);
    private static readonly Error CustomerProfileIncomplete =
        new("CUSTOMER_PROFILE_INCOMPLETE", CustomerAddressServiceMessages.CustomerProfileIncomplete);

    public async Task<Result<IReadOnlyList<CustomerAddressDto>>> GetAddressesAsync(Guid identityUserId, CancellationToken ct = default)
    {
        var customer = await customers.GetWithAddressesByIdentityUserIdAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure<IReadOnlyList<CustomerAddressDto>>(CustomerNotFound);

        var addresses = await locations.GetAddressesAsync(OwnedAddressIds(customer), ct);
        return Result.Success<IReadOnlyList<CustomerAddressDto>>(addresses.Select(ToDto).ToList());
    }

    public async Task<Result<CustomerAddressDto>> GetAddressAsync(Guid identityUserId, long addressId, CancellationToken ct = default)
    {
        var customer = await customers.GetWithAddressesByIdentityUserIdAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure<CustomerAddressDto>(CustomerNotFound);
        if (!Owns(customer, addressId))
            return Result.Failure<CustomerAddressDto>(AddressNotFound);

        var address = (await locations.GetAddressesAsync([addressId], ct)).FirstOrDefault();
        return address is null
            ? Result.Failure<CustomerAddressDto>(AddressNotFound)
            : Result.Success(ToDto(address));
    }

    public async Task<Result<CustomerAddressDto>> CreateAddressAsync(Guid identityUserId, CustomerAddressInput input, CancellationToken ct = default)
    {
        var customer = await customers.GetWithAddressesByIdentityUserIdAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure<CustomerAddressDto>(CustomerNotFound);

        var validated = await ValidateAsync(customer, input, ct);
        if (validated.IsFailure)
            return Result.Failure<CustomerAddressDto>(validated.Error!);
        var v = validated.Value!;

        // A customer-created address is never primary; it becomes primary only via set-default.
        var created = await locations.CreateAddressAsync(
            v.CityId, v.DistrictId, v.AddressText, v.PostalCode, v.RecipientName, v.RecipientMobile,
            isPrimary: false, ct: ct);
        if (created.IsFailure)
            return Result.Failure<CustomerAddressDto>(created.Error!);

        customer.AddAddress(CustomerAddress.Create(idGenerator.Generate(), customer.Id, created.Value!.id));
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToDto(created.Value));
    }

    public async Task<Result<CustomerAddressDto>> UpdateAddressAsync(Guid identityUserId, long addressId, CustomerAddressInput input, CancellationToken ct = default)
    {
        var customer = await customers.GetWithAddressesByIdentityUserIdAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure<CustomerAddressDto>(CustomerNotFound);
        if (!Owns(customer, addressId))
            return Result.Failure<CustomerAddressDto>(AddressNotFound);

        var validated = await ValidateAsync(customer, input, ct);
        if (validated.IsFailure)
            return Result.Failure<CustomerAddressDto>(validated.Error!);
        var v = validated.Value!;

        var updated = await locations.UpdateAddressAsync(
            addressId, v.CityId, v.DistrictId, v.AddressText, v.PostalCode, v.RecipientName, v.RecipientMobile, ct);
        return updated.IsSuccess
            ? Result.Success(ToDto(updated.Value!))
            : Result.Failure<CustomerAddressDto>(updated.Error!);
    }

    public async Task<Result> DeleteAddressAsync(Guid identityUserId, long addressId, CancellationToken ct = default)
    {
        var customer = await customers.GetWithAddressesByIdentityUserIdAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure(CustomerNotFound);
        if (!Owns(customer, addressId))
            return Result.Failure(AddressNotFound);

        return await locations.DeleteAddressAsync(addressId, ct);
    }

    public async Task<Result<CustomerAddressDto>> SetDefaultAddressAsync(Guid identityUserId, long addressId, CancellationToken ct = default)
    {
        var customer = await customers.GetWithAddressesByIdentityUserIdAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure<CustomerAddressDto>(CustomerNotFound);
        if (!Owns(customer, addressId))
            return Result.Failure<CustomerAddressDto>(AddressNotFound);

        var result = await locations.SetPrimaryAddressAsync(addressId, OwnedAddressIds(customer), ct);
        return result.IsSuccess
            ? Result.Success(ToDto(result.Value!))
            : Result.Failure<CustomerAddressDto>(result.Error!);
    }

    private async Task<Result<ValidatedAddress>> ValidateAsync(CustomerAggregate customer, CustomerAddressInput input, CancellationToken ct)
    {
        if (input.PostalCode is null || !PostalCodeRegex.IsMatch(input.PostalCode))
            return Result.Failure<ValidatedAddress>(InvalidPostalCode);

        var addressText = string.IsNullOrWhiteSpace(input.AddressText) ? null : input.AddressText.Trim();
        if (addressText is { Length: > AddressTextMaxLength })
            return Result.Failure<ValidatedAddress>(AddressTextTooLong);

        var city = input.CityId > 0 ? await locations.GetCityById(input.CityId, ct) : null;
        if (city is null || city.IsFailure)
            return Result.Failure<ValidatedAddress>(InvalidCity);
        if (input.DistrictId is not null && city.Value!.GetDistrictById(input.DistrictId.Value) is null)
            return Result.Failure<ValidatedAddress>(InvalidDistrict);

        var recipient = ResolveRecipient(customer, input);
        if (recipient.IsFailure)
            return Result.Failure<ValidatedAddress>(recipient.Error!);

        return Result.Success(new ValidatedAddress(
            input.CityId, input.DistrictId, addressText, input.PostalCode,
            recipient.Value.Name, recipient.Value.Mobile));
    }

    private static Result<(string Name, string Mobile)> ResolveRecipient(CustomerAggregate customer, CustomerAddressInput input)
    {
        if (input.IsSelfRecipient)
        {
            var name = $"{customer.FirstName} {customer.LastName}".Trim();
            if (name.Length == 0)
                return Result.Failure<(string, string)>(CustomerProfileIncomplete);
            return Result.Success((name, customer.MobileNumber));
        }

        if (string.IsNullOrWhiteSpace(input.RecipientName))
            return Result.Failure<(string, string)>(RecipientNameRequired);
        var recipientName = input.RecipientName.Trim();
        if (recipientName.Length > RecipientNameMaxLength)
            return Result.Failure<(string, string)>(RecipientNameTooLong);

        try
        {
            return Result.Success((recipientName, MobileNumber.Create(input.RecipientMobile ?? string.Empty).Value));
        }
        catch (DomainException dex)
        {
            return Result.Failure<(string, string)>(new Error(dex.Code, dex.Message));
        }
    }

    private static bool Owns(CustomerAggregate customer, long addressId)
        => customer.Addresses.Any(a => a.AddressId == addressId);

    private static List<long> OwnedAddressIds(CustomerAggregate customer)
        => customer.Addresses.Select(a => a.AddressId).ToList();

    private static CustomerAddressDto ToDto(AddressResponse a) => new(
        a.id, a.cityId, a.districtId, a.addressText, a.postalCode,
        a.recipientName, a.recipientMobile, a.isPrimary, a.statusId, a.createdAt, a.updatedAt);

    private sealed record ValidatedAddress(
        int CityId, int? DistrictId, string? AddressText, string PostalCode, string RecipientName, string RecipientMobile);
}
