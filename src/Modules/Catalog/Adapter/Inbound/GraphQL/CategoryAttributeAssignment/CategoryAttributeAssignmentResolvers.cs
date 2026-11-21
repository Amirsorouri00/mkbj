using Mkbj.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Inbound;
using Mkbj.Modules.Catalog.QueryApplication.Abstractions;
using Mkbj.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;
using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using Mkbj.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.CategoryAttributeAssignment;

[ExtendObjectType("Query")]
public sealed class CategoryAttributeAssignmentQueryResolvers
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<IReadOnlyList<CategoryAttributeAssignmentSnapshot>> CategoryAttributeAssignments(
        [GraphQLType(typeof(NonNullType<IdType>))] long categoryId,
        [Service] ICategoryAttributeAssignmentService assignments,
        CancellationToken ct)
    {
        var result = await assignments.ListByCategoryAsync(categoryId, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<IReadOnlyList<CategoryAttributeAssignmentAdminRow>> CategoryAttributeAdminRows(
        [GraphQLType(typeof(NonNullType<IdType>))] long categoryId,
        [Service] ICategoryAttributeAssignmentQueryService queryService,
        CancellationToken ct)
        => queryService.GetAdminRowsByCategoryAsync(categoryId, ct);
}

[ExtendObjectType("Mutation")]
public sealed class CategoryAttributeAssignmentMutationResolvers
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> AssignCategoryAttribute(
        [GraphQLType(typeof(NonNullType<IdType>))] long categoryId,
        [GraphQLType(typeof(NonNullType<IdType>))] long attributeBlueprintId,
        bool isRequired,
        bool isFilterable,
        bool isSearchable,
        bool isInTitle,
        int? titleOrder,
        bool isPrimary,
        bool isVariantGenerating,
        [Service] ICategoryAttributeAssignmentService assignments,
        CancellationToken ct)
    {
        var result = await assignments.AssignAsync(
            categoryId, attributeBlueprintId, isRequired, isFilterable, isSearchable, isInTitle, titleOrder,
            isPrimary, isVariantGenerating, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> UnassignCategoryAttribute(
        [GraphQLType(typeof(NonNullType<IdType>))] long assignmentId,
        [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.UnassignAsync(assignmentId, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributeRequired(
        [GraphQLType(typeof(NonNullType<IdType>))] long assignmentId,
        bool isRequired, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetRequiredAsync(assignmentId, isRequired, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributeFilterable(
        [GraphQLType(typeof(NonNullType<IdType>))] long assignmentId,
        bool isFilterable, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetFilterableAsync(assignmentId, isFilterable, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributeSearchable(
        [GraphQLType(typeof(NonNullType<IdType>))] long assignmentId,
        bool isSearchable, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetSearchableAsync(assignmentId, isSearchable, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributeTitleConfiguration(
        [GraphQLType(typeof(NonNullType<IdType>))] long assignmentId,
        bool isInTitle, int? titleOrder, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetTitleConfigurationAsync(assignmentId, isInTitle, titleOrder, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> ReorderCategoryAttribute(
        [GraphQLType(typeof(NonNullType<IdType>))] long assignmentId,
        int titleOrder, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.ReorderAsync(assignmentId, titleOrder, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributePrimary(
        [GraphQLType(typeof(NonNullType<IdType>))] long assignmentId,
        bool isPrimary, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetPrimaryAsync(assignmentId, isPrimary, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributeVariantGenerating(
        [GraphQLType(typeof(NonNullType<IdType>))] long assignmentId,
        bool isVariantGenerating, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetVariantGeneratingAsync(assignmentId, isVariantGenerating, ct);
        return result.ThrowIfFailure();
    }
}
