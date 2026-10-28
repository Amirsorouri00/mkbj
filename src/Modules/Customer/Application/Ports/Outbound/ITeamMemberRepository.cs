using EwaysVNext.Modules.Customer.Application.Domain.TeamMembers;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;

public interface ITeamMemberRepository
{
    Task<TeamMember?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<TeamMember?> GetByIdentityUserIdAsync(Guid identityUserId, CancellationToken ct = default);

    Task<bool> ExistsForMobileNumberAsync(MobileNumber mobileNumber, long? excludeId = null, CancellationToken ct = default);

    Task<bool> ExistsForNationalCodeAsync(NationalCode nationalCode, long? excludeId = null, CancellationToken ct = default);

    Task<bool> ExistsForPersonnelCodeAsync(string personnelCode, long? excludeId = null, CancellationToken ct = default);

    Task AddAsync(TeamMember teamMember, CancellationToken ct = default);

    void Update(TeamMember teamMember);

    IQueryable<TeamMemberListItem> QueryForList(TeamMemberListFilter filter);
}
