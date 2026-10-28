using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;

public sealed record CustomerAdminListFilter(
    string? Name = null,
    string? Family = null,
    string? UserCode = null,
    string? NationalId = null,
    string? PhoneNumber = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int? ProvinceId = null,
    int? CityId = null,
    int? DistrictId = null,
    IReadOnlyCollection<CustomerStatusType>? RegistrationStatuses = null);
