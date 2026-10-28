using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;

public sealed record CustomerProfile(
    SnowflakeId Id,
    Guid IdentityUserId,
    string MobileNumber,
    string FirstName,
    string LastName,
    string NationalCode,
    Gender? Gender,
    DateOnly? BirthDate,
    string? EconomicCode,
    int? ActivityStatusId,
    int? VerificationStatusId,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
