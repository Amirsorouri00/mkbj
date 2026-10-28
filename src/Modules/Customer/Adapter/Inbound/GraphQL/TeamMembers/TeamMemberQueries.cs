using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.TeamMembers;

[ExtendObjectType(typeof(Query))]
public sealed class TeamMemberQueries
{
    [UseOffsetPaging(IncludeTotalCount = true)]
    public IQueryable<TeamMemberListItem> TeamMembers(
        [Service] ITeamMemberRepository repository,
        string? search = null,
        TeamMemberStatusType? status = null) =>
        repository.QueryForList(new TeamMemberListFilter(search, status));
}
