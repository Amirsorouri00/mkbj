namespace EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;

/// <summary>
/// One of the customer's addresses. <see cref="IsPrimary"/> marks the customer's
/// primary/default delivery address (initially the registration address).
/// </summary>
public sealed record CustomerAddressDto(
    long Id,
    int CityId,
    int? DistrictId,
    string? AddressText,
    string? PostalCode,
    string? RecipientName,
    string? RecipientMobile,
    bool IsPrimary,
    int StatusId,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>
/// Customer-editable address fields. When <see cref="IsSelfRecipient"/> is true the recipient
/// name/mobile are taken from the customer's profile and any supplied values are ignored.
/// </summary>
public sealed record CustomerAddressInput(
    int CityId,
    int? DistrictId,
    string? AddressText,
    string? PostalCode,
    bool IsSelfRecipient,
    string? RecipientName,
    string? RecipientMobile);
