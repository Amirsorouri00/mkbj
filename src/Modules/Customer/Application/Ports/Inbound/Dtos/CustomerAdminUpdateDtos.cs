using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;

public sealed record AdminCustomerUpdate(
    Gender Gender,
    string? EconomicCode,
    string? IntaCode,
    string? RoleCode,
    string? Email,
    string? SecondaryPhoneNumber,
    IReadOnlyCollection<int> JobTypeIds,
    AdminCustomerAddressUpdate? Address);
public sealed record AdminCustomerAddressUpdate(
    int ProvinceId,
    int CityId,
    int? DistrictId,
    string? AddressText,
    string? PostalCode);
