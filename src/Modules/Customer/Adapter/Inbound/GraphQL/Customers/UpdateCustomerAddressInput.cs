namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

public sealed record UpdateCustomerAddressInput(
    int ProvinceId,
    int CityId,
    int? DistrictId = null,
    string? AddressText = null,
    string? PostalCode = null);
