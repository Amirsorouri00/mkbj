using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Inbound;
using EwaysVNext.Modules.Catalog.QueryApplication.Abstractions;
using EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;
using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.CategoryAttributeAssignment;

[ExtendObjectType("Query")]
public sealed class CategoryAttributeAssignmentQueryResolvers
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<IReadOnlyList<CategoryAttributeAssignmentSnapshot>> CategoryAttributeAssignments(
        long categoryId,
        [Service] ICategoryAttributeAssignmentService assignments,
        CancellationToken ct)
    {
        var result = await assignments.ListByCategoryAsync(categoryId, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<IReadOnlyList<CategoryAttributeAssignmentAdminRow>> CategoryAttributeAdminRows(
        long categoryId,
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
        long categoryId,
        long attributeBlueprintId,
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
        long assignmentId, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.UnassignAsync(assignmentId, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributeRequired(
        long assignmentId, bool isRequired, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetRequiredAsync(assignmentId, isRequired, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributeFilterable(
        long assignmentId, bool isFilterable, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetFilterableAsync(assignmentId, isFilterable, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributeSearchable(
        long assignmentId, bool isSearchable, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetSearchableAsync(assignmentId, isSearchable, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributeTitleConfiguration(
        long assignmentId, bool isInTitle, int? titleOrder, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetTitleConfigurationAsync(assignmentId, isInTitle, titleOrder, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> ReorderCategoryAttribute(
        long assignmentId, int titleOrder, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.ReorderAsync(assignmentId, titleOrder, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributePrimary(
        long assignmentId, bool isPrimary, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetPrimaryAsync(assignmentId, isPrimary, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<CategoryAttributeAssignmentSnapshot> SetCategoryAttributeVariantGenerating(
        long assignmentId, bool isVariantGenerating, [Service] ICategoryAttributeAssignmentService assignments, CancellationToken ct)
    {
        var result = await assignments.SetVariantGeneratingAsync(assignmentId, isVariantGenerating, ct);
        return result.ThrowIfFailure();
    }
}
