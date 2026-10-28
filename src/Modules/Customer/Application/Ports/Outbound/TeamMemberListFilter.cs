using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;

public sealed record TeamMemberListFilter(string? Search = null, TeamMemberStatusType? Status = null);
