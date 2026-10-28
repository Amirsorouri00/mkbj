using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.TeamMembers;

[ExtendObjectType(typeof(Mutation))]
public sealed class TeamMemberMutations
{
    public async Task<CreateTeamMemberPayload> CreateTeamMember(
        CreateTeamMemberInput input,
        [Service] ITeamMemberInboundPort port,
        CancellationToken cancellationToken)
    {
        var id = (await port.CreateAsync(
            input.FirstName,
            input.LastName,
            input.Gender,
            input.NationalId,
            input.DateOfBirth,
            input.MobileNumber,
            input.PersonnelCode,
            input.Department,
            input.JobTitle,
            cancellationToken)).ValueOrThrow();

        return new CreateTeamMemberPayload(id);
    }

    public async Task<UpdateTeamMemberPayload> UpdateTeamMember(
        UpdateTeamMemberInput input,
        [Service] ITeamMemberInboundPort port,
        CancellationToken cancellationToken)
    {
        var result = await port.UpdateAsync(
            input.Id,
            input.FirstName,
            input.LastName,
            input.Gender,
            input.NationalId,
            input.DateOfBirth,
            input.MobileNumber,
            input.PersonnelCode,
            input.Department,
            input.JobTitle,
            cancellationToken);

        return result.ValueOrThrow(new UpdateTeamMemberPayload(true));
    }

    public async Task<DeleteTeamMemberPayload> DeleteTeamMember(
        DeleteTeamMemberInput input,
        [Service] ITeamMemberInboundPort port,
        CancellationToken cancellationToken)
    {
        var result = await port.DeleteAsync(input.Id, Guid.Empty, cancellationToken);

        return result.ValueOrThrow(new DeleteTeamMemberPayload(true));
    }
}
