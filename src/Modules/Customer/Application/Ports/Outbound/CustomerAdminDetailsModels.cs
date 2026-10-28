using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;

public sealed record CustomerAdminDetails(
    long Id,
    string? FirstName,
    string? LastName,
    string? NationalCode,
    string MobileNumber,
    string? Email,
    string? SecondaryPhoneNumber,
    Gender? Gender,
    DateOnly? BirthDate,
    string? EconomicCode,
    string? IntaCode,
    string? RoleCode,
    DateTime RegistrationDate,
    int? StatusId,
    int? VerificationStatusId,
    int? VerificationMethod,
    string? Reason,
    int? ProvinceId,
    string? ProvinceName,
    int? CityId,
    string? CityName,
    int? DistrictId,
    string? DistrictName,
    string? AddressText,
    string? PostalCode,
    IReadOnlyList<JobTypeItem> JobTypes);
