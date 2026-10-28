using EwaysVNext.Database;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;
using TeamMemberAggregate = EwaysVNext.Modules.Customer.Application.Domain.TeamMembers.TeamMember;

namespace EwaysVNext.Modules.Customer.Adapter.Outbound.Repositories;

internal sealed class TeamMemberRepository(EwaysVNextDbContext db)
    : EfCoreRepository<TeamMemberAggregate, long>(db), ITeamMemberRepository
{
    public new async Task<TeamMemberAggregate?> GetByIdAsync(long id, CancellationToken ct = default)
        => await db.TeamMembers.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<TeamMemberAggregate?> GetByIdentityUserIdAsync(Guid identityUserId, CancellationToken ct = default)
        => await db.TeamMembers.FirstOrDefaultAsync(t => t.IdentityUserId == identityUserId, ct);

    public async Task<bool> ExistsForMobileNumberAsync(MobileNumber mobileNumber, long? excludeId = null, CancellationToken ct = default)
        => await db.TeamMembers.AnyAsync(t => t.MobileNumber == mobileNumber.Value && (excludeId == null || t.Id != excludeId), ct);

    public async Task<bool> ExistsForNationalCodeAsync(NationalCode nationalCode, long? excludeId = null, CancellationToken ct = default)
        => await db.TeamMembers.AnyAsync(t => t.NationalCode == nationalCode.Value && (excludeId == null || t.Id != excludeId), ct);

    public async Task<bool> ExistsForPersonnelCodeAsync(string personnelCode, long? excludeId = null, CancellationToken ct = default)
        => await db.TeamMembers.AnyAsync(t => t.PersonnelCode == personnelCode && (excludeId == null || t.Id != excludeId), ct);

    public override async Task AddAsync(TeamMemberAggregate teamMember, CancellationToken ct = default)
        => await db.TeamMembers.AddAsync(teamMember, ct);

    public void Update(TeamMemberAggregate teamMember)
        => db.TeamMembers.Update(teamMember);

    public IQueryable<TeamMemberListItem> QueryForList(TeamMemberListFilter filter)
    {
        var query = db.TeamMembers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(t =>
                t.FirstName.Contains(search) ||
                t.LastName.Contains(search) ||
                t.NationalCode.Contains(search) ||
                t.MobileNumber.Contains(search));
        }

        if (filter.Status is not null)
            query = query.Where(t => t.Status == filter.Status.Value);

        return query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TeamMemberListItem(
                t.Id,
                t.FirstName,
                t.LastName,
                t.Gender,
                t.NationalCode,
                t.DateOfBirth,
                t.MobileNumber,
                t.PersonnelCode,
                t.Department,
                t.JobTitle,
                t.Status,
                t.CreatedAt,
                t.UpdatedAt));
    }
}
