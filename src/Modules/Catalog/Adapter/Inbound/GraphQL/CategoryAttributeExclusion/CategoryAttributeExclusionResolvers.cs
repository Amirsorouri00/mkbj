using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Inbound;
using EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.CategoryAttributeExclusion;

[ExtendObjectType("Mutation")]
public sealed class CategoryAttributeExclusionMutationResolvers
{
    [Authorize(Policy = "Admin")]
    public async Task<CategoryAttributeExclusionSnapshot> ExcludeInheritedCategoryAttribute(
        long categoryId,
        long sourceAssignmentId,
        [Service] ICategoryAttributeExclusionService exclusions,
        CancellationToken ct)
    {
        var result = await exclusions.ExcludeAsync(categoryId, sourceAssignmentId, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = "Admin")]
    public async Task<CategoryAttributeExclusionSnapshot> IncludeInheritedCategoryAttribute(
        long categoryId,
        long sourceAssignmentId,
        [Service] ICategoryAttributeExclusionService exclusions,
        CancellationToken ct)
    {
        var result = await exclusions.IncludeAsync(categoryId, sourceAssignmentId, ct);
        return result.ThrowIfFailure();
    }

    [Authorize(Policy = "Admin")]
    public async Task<IReadOnlyList<CategoryAttributeExclusionSnapshot>> SetCategoryAttributeExclusions(
        long categoryId,
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
    [Authorize(Policy = "Admin")]
    public async Task<IReadOnlyList<EffectiveCategoryAttributeSnapshot>> EffectiveCategoryAttributes(
        long categoryId,
        [Service] ICategoryAttributeExclusionQueryService query,
        CancellationToken ct)
        => await query.GetEffectiveAttributesAsync(categoryId, ct);
}
