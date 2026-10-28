using EwaysVNext.Modules.Customer.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.TeamMembers;

public sealed class TeamMemberType : ObjectType<TeamMemberListItem>
{
    protected override void Configure(IObjectTypeDescriptor<TeamMemberListItem> descriptor)
    {
        descriptor.Field(t => t.NationalCode).Name("nationalId");
    }
}
