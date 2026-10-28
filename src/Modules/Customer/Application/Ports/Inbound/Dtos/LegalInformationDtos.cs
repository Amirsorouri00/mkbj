using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;

public sealed record LegalInformationInput(
    string? CompanyName,
    string? NationalId,
    DateOnly RegistrationDate,
    string? RegistrationNumber,
    string? CeoName,
    string? CeoMobileNumber,
    int ProvinceId,
    int CityId,
    int? DistrictId,
    string? Address,
    string? PostalCode,
    string? LandlineNumber,
    string? Fax);

public sealed record LegalInformationDetails(
    string CompanyName,
    string NationalId,
    DateOnly RegistrationDate,
    string RegistrationNumber,
    string CeoName,
    string CeoMobileNumber,
    int ProvinceId,
    int CityId,
    int? DistrictId,
    string Address,
    string PostalCode,
    string LandlineNumber,
    string? Fax);

public sealed record LegalInformationDocumentInfo(
    string Type,
    string FileName,
    string ContentType,
    long SizeBytes);

public sealed record LegalInformationResponse(
    string Status,
    LegalInformationDetails? LegalInformation,
    IReadOnlyList<LegalInformationDocumentInfo> Documents,
    string? RejectionReason,
    IReadOnlyList<string> EditableFields,
    bool CanSubmit);
