using System.Security.Claims;

using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Inbound;
using EwaysVNext.Modules.Catalog.QueryApplication.Brands;
using EwaysVNext.Modules.Catalog.QueryApplication.Products;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using EwaysVNext.Queries.Application.Paginations;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Brands;

// Both attributes sit on every resolver, never on the class. A type-level attribute on an
// [ExtendObjectType] is merged onto the whole Query/Mutation and would gate every other module's
// fields with the catalogue's policy; GraphQLGateScopeTests fails the build on that.

[ExtendObjectType(typeof(Query))]
public sealed class BrandAdminGraphQLQueries
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<PaginatedQuery<BrandAdminRow>> AdminBrandsAsync(
        string? search, int page, int pageSize,
        [Service] ICatalogAdminQueries queries, CancellationToken ct = default) =>
        queries.ListBrandsAsync(search, page, pageSize, ct);
}

[ExtendObjectType(typeof(Mutation))]
public sealed class BrandAdminGraphQLMutations
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    [GraphQLType(typeof(NonNullType<IdType>))]
    public async Task<long> CreateBrandAsync(
        string displayName, IReadOnlyList<string>? aliases, ClaimsPrincipal user,
        [Service] IBrandAdminService service, CancellationToken ct) =>
        ResultGraphQLExtensions.ValueOrThrow(
            await service.CreateAsync(displayName, aliases ?? [], Actor(user), ct));

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<bool> RenameBrandAsync(
        [GraphQLType(typeof(NonNullType<IdType>))] long brandId, string displayName,
        ClaimsPrincipal user, [Service] IBrandAdminService service, CancellationToken ct) =>
        Act(service.RenameAsync(brandId, displayName, Actor(user), ct));

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<bool> SetBrandAliasesAsync(
        [GraphQLType(typeof(NonNullType<IdType>))] long brandId, IReadOnlyList<string> aliases,
        ClaimsPrincipal user, [Service] IBrandAdminService service, CancellationToken ct) =>
        Act(service.SetAliasesAsync(brandId, aliases ?? [], Actor(user), ct));

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<bool> SetBrandActiveAsync(
        [GraphQLType(typeof(NonNullType<IdType>))] long brandId, bool active,
        ClaimsPrincipal user, [Service] IBrandAdminService service, CancellationToken ct) =>
        Act(service.SetActiveAsync(brandId, active, Actor(user), ct));

    private static AdminActor Actor(ClaimsPrincipal user) =>
        AdminActor.TryFrom(user, out var actor)
            ? actor!
            : throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("The token passed the admin policy but carries no usable subject.")
                .SetCode("UNAUTHORIZED")
                .Build());

    private static async Task<bool> Act(Task<Result> action)
    {
        (await action).ThrowIfFailure();
        return true;
    }
}
