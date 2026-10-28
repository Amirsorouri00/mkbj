using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Dtos;

public sealed record RegisterCustomerInfoRequest(string firstName,
        string lastName,
        string nationalCode,
        Gender gender,
        DateOnly birthDate,
        int cityId,
        int? districtId,
        string? address);

public sealed record ProfileRequest(string firstName,
        string lastName,
        Gender gender,
        DateOnly birthDate);
public sealed record CustomerCreatedResponse(Guid Id);


public sealed record CustomerAddressRequest(int cityId,
        int? districtId,
        string? addressText,
        string? postalCode,
        bool isSelfRecipient,
        string? recipientName,
        string? recipientMobile);
