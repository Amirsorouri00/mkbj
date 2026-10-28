using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;

public sealed record TeamMemberListItem(
    SnowflakeId Id,
    string FirstName,
    string LastName,
    Gender Gender,
    string NationalCode,
    DateOnly DateOfBirth,
    string MobileNumber,
    string PersonnelCode,
    string Department,
    string JobTitle,
    TeamMemberStatusType Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
