using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.TeamMembers;

public sealed record DeleteTeamMemberInput(SnowflakeId Id);
