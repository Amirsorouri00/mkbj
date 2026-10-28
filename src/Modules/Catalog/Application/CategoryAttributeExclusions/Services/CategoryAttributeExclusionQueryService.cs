using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Outbound;
using EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Services;

public sealed class CategoryAttributeExclusionQueryService(
    ICategoryRepository categories,
    ICategoryAttributeAssignmentRepository assignments,
    ICategoryAttributeExclusionRepository exclusions,
    IAttributeBlueprintRepository blueprints) : ICategoryAttributeExclusionQueryService
{
    public async Task<IReadOnlyList<EffectiveCategoryAttributeSnapshot>> GetEffectiveAttributesAsync(long categoryId, CancellationToken ct = default)
    {
        var ancestorChain = await ResolveAncestorChainNearestFirstAsync(categoryId, ct);

        var direct = await assignments.ListByCategoryAsync(categoryId, ct);
        var activeDirect = direct.Where(a => !a.RemovedAt.HasValue).ToList();

        var nearestByBlueprint = new Dictionary<long, CategoryAttributeAssignment>();
        foreach (var ancestorId in ancestorChain)
        {
            var ancestorAssignments = await assignments.ListByCategoryAsync(ancestorId, ct);
            foreach (var assignment in ancestorAssignments.Where(a => !a.RemovedAt.HasValue))
            {
                if (!nearestByBlueprint.ContainsKey(assignment.AttributeBlueprintId))
                    nearestByBlueprint[assignment.AttributeBlueprintId] = assignment;
            }
        }

        var currentExclusions = await exclusions.ListByCategoryAsync(categoryId, ct);
        var excludedSourceIds = currentExclusions.Select(e => e.SourceAssignmentId).ToHashSet();

        var blueprintIds = activeDirect.Select(a => a.AttributeBlueprintId)
            .Concat(nearestByBlueprint.Values.Select(a => a.AttributeBlueprintId))
            .Distinct()
            .ToList();
        var blueprintSnapshots = await blueprints.GetSnapshotsByIdsAsync(blueprintIds, ct);
        var blueprintNameById = blueprintSnapshots.ToDictionary(b => b.Id, b => b.Name);

        var result = new List<EffectiveCategoryAttributeSnapshot>(activeDirect.Count + nearestByBlueprint.Count);

        foreach (var assignment in activeDirect)
            result.Add(ToSnapshot(assignment, blueprintNameById, isInherited: false, isExcluded: false));

        foreach (var assignment in nearestByBlueprint.Values)
            result.Add(ToSnapshot(assignment, blueprintNameById, isInherited: true, isExcluded: excludedSourceIds.Contains(assignment.Id)));

        return result;
    }

    private static EffectiveCategoryAttributeSnapshot ToSnapshot(
        CategoryAttributeAssignment assignment,
        IReadOnlyDictionary<long, string> blueprintNameById,
        bool isInherited,
        bool isExcluded)
        => new(
            assignment.Id,
            assignment.CategoryId,
            assignment.AttributeBlueprintId,
            blueprintNameById.GetValueOrDefault(assignment.AttributeBlueprintId, string.Empty),
            isInherited,
            isExcluded,
            assignment.IsRequired,
            assignment.IsFilterable,
            assignment.IsSearchable,
            assignment.IsInTitle,
            assignment.TitleOrder,
            assignment.IsPrimary,
            assignment.IsVariantGenerating);

    private async Task<IReadOnlyList<long>> ResolveAncestorChainNearestFirstAsync(long categoryId, CancellationToken ct)
    {
        var nodes = await categories.GetHierarchyAsync(categoryId, null, ct);
        var byId = nodes.ToDictionary(n => n.Id);

        var chain = new List<long>();
        var visited = new HashSet<long>();
        var currentId = byId.TryGetValue(categoryId, out var self) ? self.ParentId : null;

        while (currentId is long id && visited.Add(id) && byId.TryGetValue(id, out var node))
        {
            chain.Add(node.Id);
            currentId = node.ParentId;
        }

        return chain;
    }
}
