using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.Application.Ports.Inbound;

public interface ITeamMemberInboundPort
{
    Task<Result<long>> CreateAsync(
        string firstName,
        string lastName,
        Gender gender,
        string nationalCode,
        DateOnly dateOfBirth,
        string mobileNumber,
        string personnelCode,
        string department,
        string jobTitle,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(
        long id,
        string firstName,
        string lastName,
        Gender gender,
        string nationalCode,
        DateOnly dateOfBirth,
        string mobileNumber,
        string personnelCode,
        string department,
        string jobTitle,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes (soft, status-based) the team member. Rejects deleting the currently authenticated actor's own account.
    /// </summary>
    Task<Result> DeleteAsync(long id, Guid currentUserId, CancellationToken ct = default);
}
