using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;

public sealed record CustomerAdminListItem(
    SnowflakeId Id,
    string? FirstName,
    string? LastName,
    string? NationalCode,
    string MobileNumber,
    DateOnly? BirthDate,
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
    string? DistrictName);
