using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Categories.Commands;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using CategoryEntity = EwaysVNext.Modules.Catalog.Application.Categories.Domain.Category;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Catalog.Application.Categories.Services;

public sealed class CategoryService : Ports.Inbound.ICategoryService
{
    private static readonly Error NotFound =
        new("CATEGORY_NOT_FOUND", "Category was not found.");
    private static readonly Error ParentInvalid =
        new("CATEGORY_PARENT_INVALID", "Category parent is invalid.");
    private static readonly Error ParentInactive =
        new("CATEGORY_PARENT_INACTIVE", "Category parent must be active.");
    private static readonly Error Deleted =
        new("CATEGORY_DELETED", "A deleted category cannot be changed.");
    private static readonly Error InvalidStatus =
        new("CATEGORY_STATUS_INVALID", "Category status is invalid.");
    private static readonly Error ProductInvalid =
        new("CATEGORY_PRODUCT_INVALID", "Category product is invalid.");
    private static readonly Error Cycle = new(
        "CATEGORY_CYCLE",
        "A category cannot be moved beneath one of its descendants.");
    private static readonly Error DepthExceeded = new(
        "CATEGORY_DEPTH_EXCEEDED",
        "حداکثر عمق مجاز دسته‌بندی (۵ لایه) رعایت نشده است");
    private static readonly Error CreateNameConflict = new(
        "CATEGORY_NAME_CONFLICT",
        "دسته‌بندی با این نام در این سطح وجود دارد",
        409);
    private static readonly Error UpdateNameConflict = new(
        "CATEGORY_NAME_CONFLICT",
        "نام دسته‌بندی تکراری است",
        409);
    private static readonly Error PersistenceConflict = new(
        "CATEGORY_PERSISTENCE_CONFLICT", "Category persistence conflict.", 409);

    private readonly ICategoryRepository _categories;
    private readonly ICategoryProductRepository _assignments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionExecutor _transactionExecutor;
    private readonly ICatalogProductLookup _catalog;
    //private readonly IEventDispatcher _events;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;

    public CategoryService(ICategoryRepository categories,
        ICategoryProductRepository assignments, IUnitOfWork unitOfWork,
        ICatalogProductLookup catalog, /*IEventDispatcher events, */IClock clock,
        IIdGenerator idGenerator, ITransactionExecutor transactionExecutor)
    {
        (_categories, _assignments, _unitOfWork, _catalog, /*_events, */_clock, _idGenerator) =
        (categories, assignments, unitOfWork, catalog, /*events,*/ clock, idGenerator);
        _transactionExecutor = transactionExecutor;
    }

    public async Task<Result<long>> CreateAsync(CreateCategoryCommand command,
        CancellationToken ct = default)
    {
        CategoryEntity category;
        try
        {
            category = CategoryEntity.Create(_idGenerator.Generate(),
                command.Name, command.Slug, command.Description,
                command.ParentId, command.DisplayOrder, _clock.UtcNow);
        }
        catch (DomainException error) { return Result.Failure<long>(ToError(error)); }

        var result = await _transactionExecutor.ExecuteSerializableAsync(async token =>
        {
            var parent = await LoadExistingParentAsync(command.ParentId, token);
            if (parent.IsFailure) return Result.Failure<long>(parent.Error!);
            var hierarchy = await _categories.GetHierarchyAsync(command.ParentId, null, token);
            var parentDepth = GetAncestorDepth(command.ParentId, hierarchy, token);
            if (parentDepth.IsFailure)
                return Result.Failure<long>(parentDepth.Error!);
            if (parentDepth.Value >= 5)
                return Result.Failure<long>(DepthExceeded);
            if (await _categories.NameExistsAtLevelAsync(
                    category.NormalizedName, category.ParentId, null, token))
                return Result.Failure<long>(CreateNameConflict);
            if (await _categories.SlugExistsAsync(category.Slug.Value, null, token))
                return Result.Failure<long>(SlugConflict(category.Slug.Value));

            await _categories.AddAsync(category, token);

            await _unitOfWork.SaveChangesAsync(token);

            return Result.Success<long>(category.Id);
        }, ct);

        //if (result.IsSuccess) await DispatchAndClearAsync(category, ct);
        return result;
    }

    public async Task<Result<CategoryAdminDto>> UpdateAsync(UpdateCategoryCommand command,
        CancellationToken ct = default)
    {
        var input = ValidateCategoryId(command.CategoryId);
        if (input.IsFailure) return Result.Failure<CategoryAdminDto>(input.Error!);
        if (command.Status is { } requestedStatus && !Enum.IsDefined(requestedStatus))
            return Result.Failure<CategoryAdminDto>(InvalidStatus);
        try { CategoryEntity.ValidateDetails(command.Name, command.Slug, command.Description, command.DisplayOrder); }
        catch (DomainException error) { return Result.Failure<CategoryAdminDto>(ToError(error)); }

        var result = await _transactionExecutor.ExecuteSerializableAsync(async token =>
        {
            var category = await _categories.GetByIdAsync(command.CategoryId, token);
            if (category is null) return Result.Failure<CategoryAdminDto>(NotFound);
            if (category.Status == CategoryStatus.Deleted)
                return Result.Failure<CategoryAdminDto>(Deleted);
            var normalizedName = CategoryEntity.NormalizeNameForComparison(command.Name);
            if (await _categories.NameExistsAtLevelAsync(
                    normalizedName, category.ParentId, command.CategoryId, token))
                return Result.Failure<CategoryAdminDto>(UpdateNameConflict);
            CategorySlug slug;
            try { slug = CategorySlug.Create(command.Slug); }
            catch (DomainException error) { return Result.Failure<CategoryAdminDto>(ToError(error)); }
            if (await _categories.SlugExistsAsync(slug.Value, command.CategoryId, token))
                return Result.Failure<CategoryAdminDto>(SlugConflict(slug.Value));

            StatusTransition? transition = null;
            var transitionTarget = default(CategoryStatus);
            var targetStatus = command.Status;
            if (targetStatus.HasValue && targetStatus.Value != category.Status)
            {
                transitionTarget = targetStatus.Value;
                var transitionResult = await PrepareStatusTransitionAsync(category,
                    transitionTarget,
                    token);

                if (transitionResult.IsFailure)
                    return Result.Failure<CategoryAdminDto>(transitionResult.Error!);
                transition = transitionResult.Value;
            }

            try
            {
                var now = _clock.UtcNow;

                category.Update(command.Name,
                    slug.Value,
                    command.Description,
                    command.DisplayOrder,
                    now);

                if (transition is not null)
                {
                    ApplyStatusTransition(
                        transition,
                        transitionTarget,
                        now,
                        token);
                }
            }
            catch (DomainException error)
            {
                return Result.Failure<CategoryAdminDto>(ToError(error));
            }

            await _unitOfWork.SaveChangesAsync(token);

            return Result.Success<CategoryAdminDto>(ToAdminDto(category));

        }, ct);

        return result;
    }

    public async Task<Result> MoveAsync(MoveCategoryCommand command,
        CancellationToken ct = default)
    {
        var input = ValidateCategoryId(command.CategoryId);
        if (input.IsFailure) return input;
        try { CategoryEntity.ValidateMove(command.ParentId, command.DisplayOrder); }
        catch (DomainException error) { return Result.Failure(ToError(error)); }
        if (command.ParentId == command.CategoryId)
            return Result.Failure(new Error(
                "CATEGORY_PARENT_INVALID",
                "A category cannot be its own parent."));

        CategoryEntity? changed = null;
        var result = await _transactionExecutor.ExecuteSerializableAsync(async token =>
        {
            var category = await _categories.GetByIdAsync(command.CategoryId, token);
            if (category is null) return Result.Failure(NotFound);
            if (category.Status == CategoryStatus.Deleted) return Result.Failure(Deleted);
            var parent = await LoadExistingParentAsync(command.ParentId, token);
            if (parent.IsFailure) return Result.Failure(parent.Error!);
            var hierarchy = await _categories.GetHierarchyAsync(command.ParentId, category.Id, token);
            var validation = ValidateMoveHierarchy(category.Id, command.ParentId, hierarchy, token);
            if (validation.IsFailure) return validation;

            try { category.Move(command.ParentId, command.DisplayOrder, _clock.UtcNow); }
            catch (DomainException error) { return Result.Failure(ToError(error)); }
            changed = category;

            await _unitOfWork.SaveChangesAsync(token);

            return Result.Success();
        }, ct);

        //if (result.IsSuccess) await DispatchAndClearAsync(changed!, ct);
        return result;
    }

    public async Task<Result<CategoryAdminDto>> SetStatusAsync(
        SetCategoryStatusCommand command, CancellationToken ct = default)
    {
        var input = ValidateCategoryId(command.CategoryId);
        if (input.IsFailure) return Result.Failure<CategoryAdminDto>(input.Error!);
        if (!Enum.IsDefined(command.Status))
            return Result.Failure<CategoryAdminDto>(InvalidStatus);

        return await _transactionExecutor.ExecuteSerializableAsync(async token =>
        {
            var category = await _categories.GetByIdAsync(command.CategoryId, token);
            if (category is null) return Result.Failure<CategoryAdminDto>(NotFound);
            if (category.Status == CategoryStatus.Deleted)
                return Result.Failure<CategoryAdminDto>(Deleted);
            if (command.Status != category.Status)
            {
                var transition = await PrepareStatusTransitionAsync(category, command.Status, token);

                if (transition.IsFailure)
                    return Result.Failure<CategoryAdminDto>(transition.Error!);

                try
                {
                    ApplyStatusTransition(
                        transition.Value!, command.Status, _clock.UtcNow, token);
                }
                catch (DomainException error)
                {
                    return Result.Failure<CategoryAdminDto>(ToError(error));
                }
            }

            await _unitOfWork.SaveChangesAsync(token);
            return Result.Success(ToAdminDto(category));
        }, ct);
    }

    public async Task<Result> DeactivateManyAsync(BulkDeactivateCategoriesCommand command,
        CancellationToken ct = default)
    {
        var categoryIds = command.CategoryIds?.Distinct().ToArray() ?? [];
        if (categoryIds.Length == 0 || categoryIds.Any(categoryId => categoryId <= 0))
            return Result.Failure(NotFound);

        var result = await _transactionExecutor.ExecuteSerializableAsync(async token =>
        {
            var subtree = await _categories.GetSubtreesForUpdateAsync(categoryIds, token);
            token.ThrowIfCancellationRequested();
            var uniqueSubtree = subtree.DistinctBy(item => item.Id).ToArray();
            var selected = uniqueSubtree
                .Where(item => categoryIds.Contains(item.Id))
                .ToDictionary(item => item.Id);
            if (categoryIds.Any(categoryId => !selected.ContainsKey(categoryId)))
                return Result.Failure(NotFound);
            if (selected.Values.Any(category => category.Status == CategoryStatus.Deleted))
                return Result.Failure(Deleted);

            var now = _clock.UtcNow;
            foreach (var item in uniqueSubtree)
            {
                token.ThrowIfCancellationRequested();
                if (item.Status != CategoryStatus.Deleted)
                    item.SetStatus(CategoryStatus.Inactive, parentIsActive: false,
                        hasDependencies: false, now);
            }
            await _unitOfWork.SaveChangesAsync(token);

            return Result.Success();
        }, ct);

        return result;
    }

    public async Task<Result> DeleteAsync(DeleteCategoryCommand command,
        CancellationToken ct = default) => ToResult(await SetStatusAsync(
            new SetCategoryStatusCommand(command.CategoryId, CategoryStatus.Deleted), ct));

    public async Task<Result> AssignProductAsync(AssignCategoryProductCommand command,
        CancellationToken ct = default)
    {
        CategoryProduct assignment;
        try
        {
            assignment = CategoryProduct.Create(command.CategoryId, command.ProductId,
                _clock.UtcNow, command.AssignedByUserId);
        }
        catch (DomainException error) { return Result.Failure(ToError(error)); }

        return await _transactionExecutor.ExecuteSerializableAsync(async token =>
        {
            var category = await _categories.GetByIdAsync(
                command.CategoryId,
                token);

            if (category is null)
                return Result.Failure(NotFound);

            if (category.Status == CategoryStatus.Deleted)
                return Result.Failure(Deleted);

            if (await _assignments.ExistsAsync(
                command.CategoryId,
                command.ProductId,
                token))
                return Result.Success();

            if (!await _catalog.ExistsAndIsActiveAsync(
                command.ProductId,
                token))
                return Result.Failure(ProductInvalid);

            //var outcome = await _assignments.AddIfAbsentAsync(assignment, token);
            await _assignments.AddAsync(assignment, token);
            await _unitOfWork.SaveChangesAsync(token);

            return AssociationResult(AssociationMutationOutcome.Applied);
        }, ct);
    }

    public async Task<Result> UnassignProductAsync(UnassignCategoryProductCommand command,
        CancellationToken ct = default)
    {
        if (command.CategoryId <= 0 || command.ProductId <= 0)
            return Result.Failure(ProductInvalid);
        var category = await _categories.GetByIdAsync(command.CategoryId, ct);
        if (category is null) return Result.Failure(NotFound);
        if (category.Status == CategoryStatus.Deleted) return Result.Failure(Deleted);
        var outcome = await _assignments.RemoveIfPresentAsync(
            command.CategoryId, command.ProductId, ct);
        return AssociationResult(outcome);
    }

    private async Task<Result<StatusTransition>> PrepareStatusTransitionAsync(
        CategoryEntity category, CategoryStatus targetStatus, CancellationToken ct)
    {
        if (targetStatus == CategoryStatus.Active)
        {
            var parent = await LoadActiveParentAsync(category.ParentId, ct);
            return parent.IsFailure
                ? Result.Failure<StatusTransition>(parent.Error!)
                : Result.Success(new StatusTransition(
                    [category], parent.Value?.Status == CategoryStatus.Active));
        }

        if (targetStatus == CategoryStatus.Inactive)
        {
            var subtree = await _categories.GetSubtreeForUpdateAsync(category.Id, ct);
            ct.ThrowIfCancellationRequested();
            return Result.Success(new StatusTransition(
                subtree.DistinctBy(item => item.Id).ToArray(), false));
        }

        if (targetStatus == CategoryStatus.Deleted)
        {
            var hasChildren = await _categories.HasChildrenAsync(category.Id, ct);
            var hasProducts = await _assignments.HasAnyForCategoryAsync(category.Id, ct);
            if (hasChildren || hasProducts)
            {
                try
                {
                    category.SetStatus(CategoryStatus.Deleted, false, true, _clock.UtcNow);
                }
                catch (DomainException error)
                {
                    return Result.Failure<StatusTransition>(ToError(error));
                }
            }

            return Result.Success(new StatusTransition([category], false));
        }

        return Result.Failure<StatusTransition>(InvalidStatus);
    }

    private static void ApplyStatusTransition(
        StatusTransition transition, CategoryStatus targetStatus,
        DateTimeOffset now, CancellationToken ct)
    {
        foreach (var category in transition.Categories)
        {
            ct.ThrowIfCancellationRequested();
            if (category.Status == CategoryStatus.Deleted) continue;
            category.SetStatus(targetStatus, transition.ParentIsActive, false, now);
        }
    }

    private async Task<Result<CategoryEntity?>> LoadExistingParentAsync(long? parentId,
        CancellationToken ct)
    {
        if (!parentId.HasValue) return Result.Success<CategoryEntity?>(null);
        if (parentId.HasValue && parentId.Value <= 0)
            return Result.Failure<CategoryEntity?>(ParentInvalid);
        var parent = await _categories.GetByIdAsync(parentId.Value, ct);
        return parent is null || parent.Status == CategoryStatus.Deleted
            ? Result.Failure<CategoryEntity?>(ParentInvalid)
            : Result.Success<CategoryEntity?>(parent);
    }

    private async Task<Result<CategoryEntity?>> LoadActiveParentAsync(long? parentId,
        CancellationToken ct)
    {
        var result = await LoadExistingParentAsync(parentId, ct);
        if (result.IsFailure) return result;
        return result.Value is null || result.Value.Status == CategoryStatus.Active
            ? result
            : Result.Failure<CategoryEntity?>(ParentInactive);
    }


    private static Result AssociationResult(AssociationMutationOutcome outcome) =>
        outcome == AssociationMutationOutcome.PersistenceConflict
            ? Result.Failure(PersistenceConflict)
            : Result.Success();

    private static Result ToResult(Result<CategoryAdminDto> result) =>
        result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);

    //private async Task DispatchAndClearAsync(Category category, CancellationToken ct)
    //{
    //    var events = category.DomainEvents.ToArray();
    //    if (events.Length == 0) return;
    //    await _events.DispatchAsync(events, ct);
    //    category.ClearDomainEvents();
    //}

    private static Error SlugConflict(string slug) =>
        new("CATEGORY_SLUG_CONFLICT", "Category slug '" + slug + "' already exists.");

    private static Result<int> GetAncestorDepth(
        long? categoryId, IReadOnlyList<CategoryHierarchyNode> hierarchy,
        CancellationToken ct)
    {
        var byId = hierarchy.ToDictionary(node => node.Id);
        var visited = new HashSet<long>();
        var depth = 0;
        while (categoryId.HasValue)
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(categoryId.Value))
                return Result.Failure<int>(Cycle);
            if (!byId.TryGetValue(categoryId.Value, out var node))
                return Result.Failure<int>(ParentInvalid);
            depth++;
            categoryId = node.ParentId;
        }
        return Result.Success(depth);
    }

    private static Result ValidateMoveHierarchy(
        long categoryId,
        long? parentId,
        IReadOnlyList<CategoryHierarchyNode> hierarchy,
        CancellationToken ct)
    {
        var parentDepth = GetAncestorDepth(parentId, hierarchy, ct);
        if (parentDepth.IsFailure) return Result.Failure(parentDepth.Error!);

        var children = hierarchy.ToLookup(node => node.ParentId);
        var visited = new HashSet<long>();
        var pending = new Queue<(long Id, int Depth)>();
        pending.Enqueue((categoryId, 1));
        var subtreeHeight = 0;
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Dequeue();
            if (!visited.Add(current.Id)) return Result.Failure(Cycle);
            if (current.Id == parentId) return Result.Failure(Cycle);
            subtreeHeight = Math.Max(subtreeHeight, current.Depth);
            foreach (var child in children[current.Id])
                pending.Enqueue((child.Id, current.Depth + 1));
        }

        return parentDepth.Value + subtreeHeight > 5
            ? Result.Failure(DepthExceeded)
            : Result.Success();
    }

    private static CategoryAdminDto ToAdminDto(CategoryEntity category) => new(
        category.Id, category.ParentId, category.Name, category.Slug.Value,
        category.Description, category.DisplayOrder, category.Status);

    private static Error ToError(DomainException exception) => new(exception.Code, exception.Message);
    private static Result ValidateCategoryId(long categoryId) =>
        categoryId > 0 ? Result.Success() : Result.Failure(NotFound);

    private sealed record StatusTransition(
        IReadOnlyList<CategoryEntity> Categories,
        bool ParentIsActive);
}
