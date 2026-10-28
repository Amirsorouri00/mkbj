using EwaysVNext.Domain.Common;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Validation;
using EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Services;

public sealed class CategoryAttributeAssignmentService(
    ICategoryAttributeAssignmentRepository assignments,
    IUnitOfWork unitOfWork,
    IClock clock,
    IIdGenerator idGenerator,
    CategoryAttributeAssignmentValidator assignValidator,
    ReorderCategoryAttributeValidator reorderValidator) : ICategoryAttributeAssignmentService
{
    public async Task<Result<CategoryAttributeAssignmentSnapshot>> AssignAsync(
        long categoryId,
        long attributeBlueprintId,
        bool isRequired,
        bool isFilterable,
        bool isSearchable,
        bool isInTitle,
        int? titleOrder,
        bool isPrimary,
        bool isVariantGenerating,
        CancellationToken ct = default)
    {
        var validationResult = await assignValidator.ValidateAsync((categoryId, attributeBlueprintId, titleOrder), ct);
        if (!validationResult.IsValid)
            return Result.Failure<CategoryAttributeAssignmentSnapshot>(
                new Error("VALIDATION_FAILED", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))));

        var existing = await assignments.GetActiveByCategoryAndAttributeAsync(categoryId, attributeBlueprintId, ct);
        if (existing is not null)
            return Result.Failure<CategoryAttributeAssignmentSnapshot>(
                new Error("CATEGORY_ATTRIBUTE_ALREADY_ASSIGNED", "This attribute is already assigned to the category."));

        if (isPrimary && !isVariantGenerating)
        {
            var primaryLimitError = await CheckPrimaryLimitAsync(categoryId, excludingAssignmentId: null, ct);
            if (primaryLimitError is not null)
                return Result.Failure<CategoryAttributeAssignmentSnapshot>(primaryLimitError);
        }

        CategoryAttributeAssignment assignment;
        try
        {
            assignment = CategoryAttributeAssignment.Create(
                idGenerator.Generate(), categoryId, attributeBlueprintId, clock.UtcNow,
                isRequired, isFilterable, isSearchable, isInTitle, titleOrder,
                isPrimary, isVariantGenerating);
        }
        catch (DomainException dex)
        {
            return Result.Failure<CategoryAttributeAssignmentSnapshot>(new Error(dex.Code, dex.Message));
        }

        await assignments.AddAsync(assignment, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToSnapshot(assignment));
    }

    public Task<Result<CategoryAttributeAssignmentSnapshot>> UnassignAsync(long assignmentId, CancellationToken ct = default)
        => MutateAsync(assignmentId, a => a.Remove(clock.UtcNow), ct);

    public Task<Result<CategoryAttributeAssignmentSnapshot>> SetRequiredAsync(long assignmentId, bool isRequired, CancellationToken ct = default)
        => MutateAsync(assignmentId, a => a.SetRequired(isRequired, clock.UtcNow), ct);

    public Task<Result<CategoryAttributeAssignmentSnapshot>> SetFilterableAsync(long assignmentId, bool isFilterable, CancellationToken ct = default)
        => MutateAsync(assignmentId, a => a.SetFilterable(isFilterable, clock.UtcNow), ct);

    public Task<Result<CategoryAttributeAssignmentSnapshot>> SetSearchableAsync(long assignmentId, bool isSearchable, CancellationToken ct = default)
        => MutateAsync(assignmentId, a => a.SetSearchable(isSearchable, clock.UtcNow), ct);

    public Task<Result<CategoryAttributeAssignmentSnapshot>> SetTitleConfigurationAsync(long assignmentId, bool isInTitle, int? titleOrder, CancellationToken ct = default)
        => MutateAsync(assignmentId, a => a.SetTitleConfiguration(isInTitle, titleOrder, clock.UtcNow), ct);

    public async Task<Result<CategoryAttributeAssignmentSnapshot>> ReorderAsync(long assignmentId, int titleOrder, CancellationToken ct = default)
    {
        var validationResult = await reorderValidator.ValidateAsync((assignmentId, titleOrder), ct);
        if (!validationResult.IsValid)
            return Result.Failure<CategoryAttributeAssignmentSnapshot>(
                new Error("VALIDATION_FAILED", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))));

        return await MutateAsync(assignmentId, a => a.Reorder(titleOrder, clock.UtcNow), ct);
    }

    public async Task<Result<CategoryAttributeAssignmentSnapshot>> SetPrimaryAsync(long assignmentId, bool isPrimary, CancellationToken ct = default)
    {
        var assignment = await assignments.GetByIdAsync(assignmentId, ct);
        if (assignment is null)
            return Result.Failure<CategoryAttributeAssignmentSnapshot>(
                new Error("CATEGORY_ATTRIBUTE_ASSIGNMENT_NOT_FOUND", "Assignment was not found."));

        if (isPrimary && !assignment.IsPrimary)
        {
            var primaryLimitError = await CheckPrimaryLimitAsync(assignment.CategoryId, assignmentId, ct);
            if (primaryLimitError is not null)
                return Result.Failure<CategoryAttributeAssignmentSnapshot>(primaryLimitError);
        }

        try { assignment.SetPrimary(isPrimary, clock.UtcNow); }
        catch (DomainException dex) { return Result.Failure<CategoryAttributeAssignmentSnapshot>(new Error(dex.Code, dex.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ToSnapshot(assignment));
    }

    public Task<Result<CategoryAttributeAssignmentSnapshot>> SetVariantGeneratingAsync(long assignmentId, bool isVariantGenerating, CancellationToken ct = default)
        => MutateAsync(assignmentId, a => a.SetVariantGenerating(isVariantGenerating, clock.UtcNow), ct);

    public async Task<Result<IReadOnlyList<CategoryAttributeAssignmentSnapshot>>> ListByCategoryAsync(long categoryId, CancellationToken ct = default)
    {
        var list = await assignments.ListByCategoryAsync(categoryId, ct);
        return Result.Success<IReadOnlyList<CategoryAttributeAssignmentSnapshot>>(list.Select(ToSnapshot).ToList());
    }

    private async Task<Error?> CheckPrimaryLimitAsync(long categoryId, long? excludingAssignmentId, CancellationToken ct)
    {
        var activeCount = await assignments.CountActivePrimaryInCategoriesAsync(new[] { categoryId }, excludingAssignmentId, ct);
        return activeCount >= CategoryAttributeAssignment.MaxPrimaryAttributesPerCategory
            ? new Error(
                "CATEGORY_ATTRIBUTE_PRIMARY_LIMIT_REACHED",
                $"This category already has {CategoryAttributeAssignment.MaxPrimaryAttributesPerCategory} Primary attributes.")
            : null;
    }

    private async Task<Result<CategoryAttributeAssignmentSnapshot>> MutateAsync(
        long assignmentId, Action<CategoryAttributeAssignment> mutate, CancellationToken ct)
    {
        var assignment = await assignments.GetByIdAsync(assignmentId, ct);
        if (assignment is null)
            return Result.Failure<CategoryAttributeAssignmentSnapshot>(
                new Error("CATEGORY_ATTRIBUTE_ASSIGNMENT_NOT_FOUND", "Assignment was not found."));

        try
        {
            mutate(assignment);
        }
        catch (DomainException dex)
        {
            return Result.Failure<CategoryAttributeAssignmentSnapshot>(new Error(dex.Code, dex.Message));
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ToSnapshot(assignment));
    }

    private static CategoryAttributeAssignmentSnapshot ToSnapshot(CategoryAttributeAssignment a) => new(
        a.Id, a.CategoryId, a.AttributeBlueprintId,
        a.IsRequired, a.IsFilterable, a.IsSearchable, a.IsInTitle, a.TitleOrder,
        a.IsPrimary, a.IsVariantGenerating,
        a.RemovedAt.HasValue);
}
