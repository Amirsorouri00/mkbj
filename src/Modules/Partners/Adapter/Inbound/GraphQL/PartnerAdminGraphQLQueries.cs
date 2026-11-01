using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Partners.QueryApplication;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using EwaysVNext.Queries.Application.Paginations;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Partners.Adapter.Inbound.GraphQL;

/// <summary>
/// Partner reads over GraphQL. Gated the same way as the REST surface, since the same data
/// reached by a different protocol is not a different permission question.
/// </summary>
[ExtendObjectType(typeof(Query))]
public sealed class PartnerAdminGraphQLQueries
{
    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public Task<PaginatedQuery<PartnerListItem>> AdminPartnersAsync(
        int? page, int? pageSize,
        [Service] IPartnerAdminQueries queries,
        CancellationToken ct) =>
        queries.ListAsync(page ?? 1, pageSize ?? 20, ct);

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public async Task<PartnerDetail> AdminPartnerAsync(
        long id,
        [Service] IPartnerAdminQueries queries,
        [Service] IClock clock,
        CancellationToken ct) =>
        ResultGraphQLExtensions.ValueOrThrow(await queries.GetAsync(id, clock.UtcNow, ct));

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.PartnersManage)]
    public Task<PaginatedQuery<ApiKeyUsageItem>> AdminPartnerUsageAsync(
        long id, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
        [Service] IPartnerAdminQueries queries,
        CancellationToken ct) =>
        queries.GetUsageAsync(id, from, to, page ?? 1, pageSize ?? 20, ct);
}
