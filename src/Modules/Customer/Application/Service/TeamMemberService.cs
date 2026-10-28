using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Logging;
using TeamMemberAggregate = EwaysVNext.Modules.Customer.Application.Domain.TeamMembers.TeamMember;

namespace EwaysVNext.Modules.Customer.Application.Service;

public sealed class TeamMemberService(
    ITeamMemberRepository teamMembers,
    IUnitOfWork unitOfWork,
    ILogger<TeamMemberService> logger,
    IClock clock,
    IIdGenerator idGenerator) : ITeamMemberInboundPort
{
    private static readonly Error TeamMemberNotFound = new("TEAMMEMBER_NOT_FOUND", TeamMemberServiceMessages.TeamMemberNotFound);
    private static readonly Error MobileNumberAlreadyExists = new("TEAMMEMBER_MOBILENUMBER_ALREADY_EXISTS", TeamMemberServiceMessages.MobileNumberAlreadyExists);
    private static readonly Error NationalCodeAlreadyExists = new("TEAMMEMBER_NATIONALCODE_ALREADY_EXISTS", TeamMemberServiceMessages.NationalCodeAlreadyExists);
    private static readonly Error PersonnelCodeAlreadyExists = new("TEAMMEMBER_PERSONNELCODE_ALREADY_EXISTS", TeamMemberServiceMessages.PersonnelCodeAlreadyExists);
    private static readonly Error SelfDeleteNotAllowed = new("TEAMMEMBER_SELF_DELETE_NOT_ALLOWED", TeamMemberServiceMessages.SelfDeleteNotAllowed, 403);
    private static readonly Error PersistenceConflict = new("TEAMMEMBER_PERSISTENCE_CONFLICT", TeamMemberServiceMessages.PersistenceConflict, 409);

    public async Task<Result<long>> CreateAsync(
        string firstName,
        string lastName,
        Gender gender,
        string nationalCode,
        DateOnly dateOfBirth,
        string mobileNumber,
        string personnelCode,
        string department,
        string jobTitle,
        CancellationToken ct = default)
    {
        var mobile = MobileNumber.Create(mobileNumber);
        var nationalCodeVo = NationalCode.Create(nationalCode);
        var now = clock.UtcNow;

        TeamMemberAggregate.ValidateProfile(firstName, lastName, dateOfBirth, personnelCode, department, jobTitle, now);

        if (await teamMembers.ExistsForMobileNumberAsync(mobile, ct: ct))
            return Result.Failure<long>(MobileNumberAlreadyExists);

        if (await teamMembers.ExistsForNationalCodeAsync(nationalCodeVo, ct: ct))
            return Result.Failure<long>(NationalCodeAlreadyExists);

        if (await teamMembers.ExistsForPersonnelCodeAsync(personnelCode, ct: ct))
            return Result.Failure<long>(PersonnelCodeAlreadyExists);

        
        var identityUserId = Guid.NewGuid();

        var teamMember = TeamMemberAggregate.Create(
            idGenerator.Generate(),
            firstName, lastName, gender, nationalCodeVo, dateOfBirth, mobile,
            personnelCode, department, jobTitle, identityUserId, now);

        try
        {
            await teamMembers.AddAsync(teamMember, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                TeamMemberServiceMessages.PersistAfterProvisioningFailed,
                identityUserId);

            return Result.Failure<long>(PersistenceConflict);
        }

        return Result.Success(teamMember.Id);
    }

    public async Task<Result> UpdateAsync(
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
        CancellationToken ct = default)
    {
        var teamMember = await teamMembers.GetByIdAsync(id, ct);
        if (teamMember is null)
            return Result.Failure(TeamMemberNotFound);

        var mobile = MobileNumber.Create(mobileNumber);
        var nationalCodeVo = NationalCode.Create(nationalCode);
        var now = clock.UtcNow;
        TeamMemberAggregate.ValidateProfile(firstName, lastName, dateOfBirth, personnelCode, department, jobTitle, now);

        if (await teamMembers.ExistsForMobileNumberAsync(mobile, id, ct))
            return Result.Failure(MobileNumberAlreadyExists);

        if (await teamMembers.ExistsForNationalCodeAsync(nationalCodeVo, id, ct))
            return Result.Failure(NationalCodeAlreadyExists);

        if (await teamMembers.ExistsForPersonnelCodeAsync(personnelCode, id, ct))
            return Result.Failure(PersonnelCodeAlreadyExists);

        var previousMobileNumber = teamMember.MobileNumber;

        teamMember.UpdateProfile(firstName, lastName, gender, nationalCodeVo, dateOfBirth, mobile, personnelCode, department, jobTitle, now);
        teamMembers.Update(teamMember);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, TeamMemberServiceMessages.PersistProfileUpdateFailed, id);

            return Result.Failure(PersistenceConflict);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(long id, Guid currentUserId, CancellationToken ct = default)
    {
        var teamMember = await teamMembers.GetByIdAsync(id, ct);
        if (teamMember is null)
            return Result.Failure(TeamMemberNotFound);

        if (teamMember.IdentityUserId == currentUserId)
            return Result.Failure(SelfDeleteNotAllowed);

        teamMember.Delete(clock.UtcNow);
        teamMembers.Update(teamMember);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
