using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;

public sealed class CategoryAttributeAssignment : AggregateRoot<long>
{
    public const int MaxPrimaryAttributesPerCategory = 9;

    public long CategoryId { get; private set; }

    public long AttributeBlueprintId { get; private set; }

    public bool IsRequired { get; private set; }

    public bool IsFilterable { get; private set; }

    public bool IsSearchable { get; private set; }

    public bool IsInTitle { get; private set; }

    public int? TitleOrder { get; private set; }

    public bool IsPrimary { get; private set; }

    public bool IsVariantGenerating { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    private CategoryAttributeAssignment() { }

    public static CategoryAttributeAssignment Create(
        long id,
        long categoryId,
        long attributeBlueprintId,
        DateTimeOffset now,
        bool isRequired = false,
        bool isFilterable = false,
        bool isSearchable = false,
        bool isInTitle = false,
        int? titleOrder = null,
        bool isPrimary = false,
        bool isVariantGenerating = false)
    {
        if (id <= 0)
            throw new DomainException("CATEGORY_ATTRIBUTE_ASSIGNMENT_ID_INVALID", "Category attribute assignment ID must be positive.");
        if (categoryId <= 0)
            throw new DomainException("CATEGORY_ATTRIBUTE_ASSIGNMENT_CATEGORY_INVALID", "Category ID must be positive.");
        if (attributeBlueprintId <= 0)
            throw new DomainException("CATEGORY_ATTRIBUTE_ASSIGNMENT_ATTRIBUTE_INVALID", "Attribute blueprint ID must be positive.");

        EnsureTitleOrderIsValid(titleOrder);

        var effectiveIsPrimary = isPrimary && !isVariantGenerating;
        var effectiveIsRequired = isRequired || effectiveIsPrimary;

        var assignment = new CategoryAttributeAssignment
        {
            Id = id,
            CategoryId = categoryId,
            AttributeBlueprintId = attributeBlueprintId,
            IsRequired = effectiveIsRequired,
            IsFilterable = isFilterable,
            IsSearchable = isSearchable,
            IsInTitle = isInTitle,
            TitleOrder = isInTitle ? titleOrder : null,
            IsPrimary = effectiveIsPrimary,
            IsVariantGenerating = isVariantGenerating,
            RemovedAt = null,
            CreatedAt = now
        };

        assignment.Raise(new CategoryAttributeAssignedDomainEvent(assignment.Id, categoryId, attributeBlueprintId));
        return assignment;
    }

    public void SetRequired(bool isRequired, DateTimeOffset now)
    {
        EnsureNotRemoved();
        if (IsRequired == isRequired) return;

        if (!isRequired && IsPrimary)
            throw new DomainException(
                "CATEGORY_ATTRIBUTE_REQUIRED_LOCKED_BY_PRIMARY",
                "Required cannot be disabled while Primary is enabled for this attribute.");

        IsRequired = isRequired;
        Touch(now);

        Raise(isRequired
            ? new CategoryAttributeBecameRequiredDomainEvent(Id, CategoryId, AttributeBlueprintId)
            : new CategoryAttributeFlagsChangedDomainEvent(Id, CategoryId, AttributeBlueprintId));
    }

    public void SetFilterable(bool isFilterable, DateTimeOffset now)
    {
        EnsureNotRemoved();
        if (IsFilterable == isFilterable) return;

        IsFilterable = isFilterable;
        Touch(now);
        Raise(new CategoryAttributeFlagsChangedDomainEvent(Id, CategoryId, AttributeBlueprintId));
    }

    public void SetSearchable(bool isSearchable, DateTimeOffset now)
    {
        EnsureNotRemoved();
        if (IsSearchable == isSearchable) return;

        IsSearchable = isSearchable;
        Touch(now);
        Raise(new CategoryAttributeFlagsChangedDomainEvent(Id, CategoryId, AttributeBlueprintId));
    }

    public void SetTitleConfiguration(bool isInTitle, int? titleOrder, DateTimeOffset now)
    {
        EnsureNotRemoved();
        EnsureTitleOrderIsValid(titleOrder);

        var normalizedOrder = isInTitle ? titleOrder : null;
        if (IsInTitle == isInTitle && TitleOrder == normalizedOrder) return;

        IsInTitle = isInTitle;
        TitleOrder = normalizedOrder;
        Touch(now);
        Raise(new CategoryAttributeTitleConfigurationChangedDomainEvent(Id, CategoryId, AttributeBlueprintId, IsInTitle, TitleOrder));
    }

    public void Reorder(int titleOrder, DateTimeOffset now)
    {
        EnsureNotRemoved();
        if (!IsInTitle)
            throw new DomainException("CATEGORY_ATTRIBUTE_NOT_IN_TITLE", "Only an in-title attribute can be reordered.");

        EnsureTitleOrderIsValid(titleOrder);
        if (TitleOrder == titleOrder) return;

        TitleOrder = titleOrder;
        Touch(now);
        Raise(new CategoryAttributeTitleConfigurationChangedDomainEvent(Id, CategoryId, AttributeBlueprintId, IsInTitle, TitleOrder));
    }

    public void SetPrimary(bool isPrimary, DateTimeOffset now)
    {
        EnsureNotRemoved();

        if (isPrimary && IsVariantGenerating)
            throw new DomainException(
                "CATEGORY_ATTRIBUTE_PRIMARY_BLOCKED_BY_VARIANT_GENERATING",
                "Primary cannot be enabled while Variant-Generating is enabled for this attribute.");

        if (IsPrimary == isPrimary) return;

        IsPrimary = isPrimary;

        var wasRequired = IsRequired;
        if (isPrimary) IsRequired = true;

        Touch(now);

        Raise(new CategoryAttributePriorityChangedDomainEvent(Id, CategoryId, AttributeBlueprintId, IsPrimary, IsVariantGenerating));
        if (!wasRequired && IsRequired)
            Raise(new CategoryAttributeBecameRequiredDomainEvent(Id, CategoryId, AttributeBlueprintId));
    }

    public void SetVariantGenerating(bool isVariantGenerating, DateTimeOffset now)
    {
        EnsureNotRemoved();
        if (IsVariantGenerating == isVariantGenerating) return;

        IsVariantGenerating = isVariantGenerating;
        if (isVariantGenerating) IsPrimary = false;

        Touch(now);
        Raise(new CategoryAttributePriorityChangedDomainEvent(Id, CategoryId, AttributeBlueprintId, IsPrimary, IsVariantGenerating));
    }

    public void Remove(DateTimeOffset now)
    {
        if (RemovedAt.HasValue) return;

        RemovedAt = now;
        Touch(now);
        Raise(new CategoryAttributeUnassignedDomainEvent(Id, CategoryId, AttributeBlueprintId));
    }

    private static void EnsureTitleOrderIsValid(int? titleOrder)
    {
        if (titleOrder is { } order && order < 0)
            throw new DomainException("CATEGORY_ATTRIBUTE_TITLE_ORDER_INVALID", "Title order cannot be negative.");
    }

    private void EnsureNotRemoved()
    {
        if (RemovedAt.HasValue)
            throw new DomainException("CATEGORY_ATTRIBUTE_ASSIGNMENT_REMOVED", "A removed category attribute assignment cannot be modified.");
    }

    private void Touch(DateTimeOffset now) => UpdatedAt = now;
}
