using Mkbj.Modules.Catalog.Application.CategoryAttributeExclusions;
using Mkbj.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Inbound;
using Mkbj.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;
using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using Mkbj.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.CategoryAttributeExclusion;

[ExtendObjectType("Mutation")]
public sealed class CategoryAttributeExclusionMutationResolvers
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeExclusionSnapshot> ExcludeInheritedCategoryAttribute(
        [GraphQLType(typeof(NonNullType<IdType>))] long categoryId,
        [GraphQLType(typeof(NonNullType<IdType>))] long sourceAssignmentId,
        [Service] ICategoryAttributeExclusionService exclusions,
        CancellationToken ct)
    {
        var result = await exclusions.ExcludeAsync(categoryId, sourceAssignmentId, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeExclusionSnapshot> IncludeInheritedCategoryAttribute(
        [GraphQLType(typeof(NonNullType<IdType>))] long categoryId,
        [GraphQLType(typeof(NonNullType<IdType>))] long sourceAssignmentId,
        [Service] ICategoryAttributeExclusionService exclusions,
        CancellationToken ct)
    {
        var result = await exclusions.IncludeAsync(categoryId, sourceAssignmentId, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<IReadOnlyList<CategoryAttributeExclusionSnapshot>> SetCategoryAttributeExclusions(
        [GraphQLType(typeof(NonNullType<IdType>))] long categoryId,
        [GraphQLType(typeof(NonNullType<ListType<NonNullType<IdType>>>))]
        IReadOnlyList<long> excludedSourceAssignmentIds,
        [Service] ICategoryAttributeExclusionService exclusions,
        CancellationToken ct)
    {
        var result = await exclusions.SetExclusionsAsync(categoryId, excludedSourceAssignmentIds, ct);
        return result.ThrowIfFailure();
    }
}

[ExtendObjectType("Query")]
public sealed class CategoryAttributeExclusionQueryResolvers
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<IReadOnlyList<EffectiveCategoryAttributeSnapshot>> EffectiveCategoryAttributes(
        [GraphQLType(typeof(NonNullType<IdType>))] long categoryId,
        [Service] ICategoryAttributeExclusionQueryService query,
        CancellationToken ct)
        => await query.GetEffectiveAttributesAsync(categoryId, ct);
}
