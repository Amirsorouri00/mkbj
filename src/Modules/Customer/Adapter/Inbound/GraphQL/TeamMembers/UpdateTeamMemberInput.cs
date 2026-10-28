using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.TeamMembers;

public sealed record UpdateTeamMemberInput(
    SnowflakeId Id,
    string FirstName,
    string LastName,
    Gender Gender,
    string NationalId,
    DateOnly DateOfBirth,
    string MobileNumber,
    string PersonnelCode,
    string Department,
    string JobTitle);
