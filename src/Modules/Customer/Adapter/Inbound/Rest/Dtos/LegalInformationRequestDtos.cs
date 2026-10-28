namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Dtos;

public sealed record LegalInformationRequest(
    string? companyName,
    string? nationalId,
    DateOnly registrationDate,
    string? registrationNumber,
    string? ceoName,
    string? ceoMobileNumber,
    int provinceId,
    int cityId,
    int? districtId,
    string? address,
    string? postalCode,
    string? landlineNumber,
    string? fax);
