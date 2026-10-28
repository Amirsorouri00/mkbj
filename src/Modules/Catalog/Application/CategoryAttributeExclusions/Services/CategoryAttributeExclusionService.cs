using EwaysVNext.Domain.Common;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Validation;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Services;

public sealed class CategoryAttributeExclusionService(
    ICategoryAttributeExclusionRepository exclusions,
    ICategoryAttributeAssignmentRepository assignments,
    ICategoryRepository categories,
    IUnitOfWork unitOfWork,
    IClock clock,
    IIdGenerator idGenerator,
    CategoryAttributeExclusionValidator validator) : ICategoryAttributeExclusionService
{
    public async Task<Result<CategoryAttributeExclusionSnapshot>> ExcludeAsync(long categoryId, long sourceAssignmentId, CancellationToken ct = default)
    {
        var validationResult = await validator.ValidateAsync((categoryId, sourceAssignmentId), ct);
        if (!validationResult.IsValid)
            return Result.Failure<CategoryAttributeExclusionSnapshot>(
                new Error("VALIDATION", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))));

        var ancestorChain = await ResolveAncestorChainNearestFirstAsync(categoryId, ct);
        var eligibility = await ValidateExclusionEligibilityAsync(categoryId, sourceAssignmentId, ancestorChain, ct);
        if (eligibility.IsFailure)
            return Result.Failure<CategoryAttributeExclusionSnapshot>(eligibility.Error!);

        var existing = await exclusions.GetByCategoryAndSourceAsync(categoryId, sourceAssignmentId, ct);
        if (existing is not null)
            return Result.Failure<CategoryAttributeExclusionSnapshot>(
                new Error("CATEGORY_ATTRIBUTE_EXCLUSION_ALREADY_EXCLUDED", "This inherited attribute is already excluded from the category."));

        CategoryAttributeExclusion exclusion;
        try
        {
            exclusion = CategoryAttributeExclusion.Create(idGenerator.Generate(), categoryId, sourceAssignmentId, clock.UtcNow);
        }
        catch (DomainException dex)
        {
            return Result.Failure<CategoryAttributeExclusionSnapshot>(new Error(dex.Code, dex.Message));
        }

        await exclusions.AddAsync(exclusion, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new CategoryAttributeExclusionSnapshot(categoryId, sourceAssignmentId, IsExcluded: true));
    }

    public async Task<Result<CategoryAttributeExclusionSnapshot>> IncludeAsync(long categoryId, long sourceAssignmentId, CancellationToken ct = default)
    {
        var validationResult = await validator.ValidateAsync((categoryId, sourceAssignmentId), ct);
        if (!validationResult.IsValid)
            return Result.Failure<CategoryAttributeExclusionSnapshot>(
                new Error("VALIDATION", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))));

        var existing = await exclusions.GetByCategoryAndSourceAsync(categoryId, sourceAssignmentId, ct);
        if (existing is null)
            return Result.Failure<CategoryAttributeExclusionSnapshot>(
                new Error("CATEGORY_ATTRIBUTE_EXCLUSION_NOT_FOUND", "This attribute is not currently excluded from the category."));

        await exclusions.RemoveAsync(existing, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new CategoryAttributeExclusionSnapshot(categoryId, sourceAssignmentId, IsExcluded: false));
    }

    public async Task<Result<IReadOnlyList<CategoryAttributeExclusionSnapshot>>> SetExclusionsAsync(
        long categoryId, IReadOnlyList<long> excludedSourceAssignmentIds, CancellationToken ct = default)
    {
        if (categoryId <= 0)
            return Result.Failure<IReadOnlyList<CategoryAttributeExclusionSnapshot>>(
                new Error("CATEGORY_ATTRIBUTE_EXCLUSION_CATEGORY_INVALID", "Category ID must be positive."));

        var desiredIds = excludedSourceAssignmentIds.Where(id => id > 0).Distinct().ToList();

        var current = await exclusions.ListByCategoryAsync(categoryId, ct);
        var currentIdSet = current.Select(e => e.SourceAssignmentId).ToHashSet();
        var desiredIdSet = desiredIds.ToHashSet();

        var toAdd = desiredIds.Where(id => !currentIdSet.Contains(id)).ToList();
        var toRemove = current.Where(e => !desiredIdSet.Contains(e.SourceAssignmentId)).ToList();

        if (toAdd.Count > 0)
        {
            var ancestorChain = await ResolveAncestorChainNearestFirstAsync(categoryId, ct);
            var newExclusions = new List<CategoryAttributeExclusion>(toAdd.Count);

            foreach (var sourceAssignmentId in toAdd)
            {
                var eligibility = await ValidateExclusionEligibilityAsync(categoryId, sourceAssignmentId, ancestorChain, ct);
                if (eligibility.IsFailure)
                    return Result.Failure<IReadOnlyList<CategoryAttributeExclusionSnapshot>>(
                        new Error(eligibility.Error!.Code, $"Assignment {sourceAssignmentId}: {eligibility.Error.Message}"));

                try
                {
                    newExclusions.Add(CategoryAttributeExclusion.Create(idGenerator.Generate(), categoryId, sourceAssignmentId, clock.UtcNow));
                }
                catch (DomainException dex)
                {
                    return Result.Failure<IReadOnlyList<CategoryAttributeExclusionSnapshot>>(new Error(dex.Code, dex.Message));
                }
            }

            foreach (var exclusion in newExclusions)
                await exclusions.AddAsync(exclusion, ct);
        }

        foreach (var exclusion in toRemove)
            await exclusions.RemoveAsync(exclusion, ct);

        if (toAdd.Count > 0 || toRemove.Count > 0)
            await unitOfWork.SaveChangesAsync(ct);

        IReadOnlyList<CategoryAttributeExclusionSnapshot> snapshots =
            desiredIds.Select(id => new CategoryAttributeExclusionSnapshot(categoryId, id, IsExcluded: true)).ToList();
        return Result.Success(snapshots);
    }

    private async Task<Result> ValidateExclusionEligibilityAsync(
        long categoryId, long sourceAssignmentId, IReadOnlyList<long> ancestorChainNearestFirst, CancellationToken ct)
    {
        var sourceAssignment = await assignments.GetByIdAsync(sourceAssignmentId, ct);
        if (sourceAssignment is null || sourceAssignment.RemovedAt.HasValue)
            return Result.Failure(
                new Error("CATEGORY_ATTRIBUTE_ASSIGNMENT_NOT_FOUND", "The source attribute assignment was not found."));

        if (sourceAssignment.CategoryId == categoryId)
            return Result.Failure(
                new Error("CATEGORY_ATTRIBUTE_EXCLUSION_NOT_INHERITED", "An attribute assigned directly to this category cannot be excluded as inherited."));

        if (!ancestorChainNearestFirst.Contains(sourceAssignment.CategoryId))
            return Result.Failure(
                new Error("CATEGORY_ATTRIBUTE_EXCLUSION_NOT_AN_ANCESTOR", "The source attribute assignment does not belong to a real ancestor of this category."));

        var nearest = await FindNearestAncestorAssignmentAsync(sourceAssignment.AttributeBlueprintId, ancestorChainNearestFirst, ct);
        if (nearest is null || nearest.Id != sourceAssignmentId)
            return Result.Failure(
                new Error("CATEGORY_ATTRIBUTE_EXCLUSION_SHADOWED", "A nearer ancestor now provides this attribute; exclude that assignment instead."));

        return Result.Success();
    }

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

    private async Task<CategoryAttributeAssignment?> FindNearestAncestorAssignmentAsync(
        long attributeBlueprintId, IReadOnlyList<long> ancestorChainNearestFirst, CancellationToken ct)
    {
        foreach (var ancestorId in ancestorChainNearestFirst)
        {
            var candidates = await assignments.ListByCategoryAsync(ancestorId, ct);
            var match = candidates.FirstOrDefault(a => a.AttributeBlueprintId == attributeBlueprintId);
            if (match is not null) return match;
        }

        return null;
    }
}
