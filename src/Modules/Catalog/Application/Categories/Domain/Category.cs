using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using System.Globalization;
using System.Text;

namespace EwaysVNext.Modules.Catalog.Application.Categories.Domain;

public sealed class Category : AggregateRoot<long>
{
    private const int StableHashCode = 1;
    private readonly List<Category> _children = [];

    public long? ParentId { get; private set; }
    public Category? Parent { get; private set; }
    public IReadOnlyCollection<Category> Children => _children;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public CategorySlug Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public int DisplayOrder { get; private set; }
    public CategoryStatus Status { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private Category() { }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        return Id != default
            && obj is Category other
            && other.Id != default
            && Id == other.Id;
    }

    public override int GetHashCode() => StableHashCode;

    public static Category Create(
        long id,
        string name,
        string slug,
        string? description,
        long? parentId,
        int displayOrder,
        DateTimeOffset now)
    {
        ValidateParent(parentId);
        var details = ValidateDetailsCore(name, slug, description, displayOrder);

        var category = new Category
        {
            Id = id,
            ParentId = parentId,
            Name = details.Name,
            NormalizedName = NormalizeNameForComparison(details.Name),
            Slug = details.Slug,
            Description = details.Description,
            DisplayOrder = displayOrder,
            Status = CategoryStatus.Inactive,
            CreatedAt = now
        };

        category.Raise(new CategoryCreatedDomainEvent(category.Name, category.Slug.Value));
        return category;
    }
    public void Update(
        string name,
        string slug,
        string? description,
        int displayOrder,
        DateTimeOffset now)
    {
        EnsureNotDeleted();
        var details = ValidateDetailsCore(name, slug, description, displayOrder);

        Name = details.Name;
        NormalizedName = NormalizeNameForComparison(details.Name);
        Slug = details.Slug;
        Description = details.Description;
        DisplayOrder = displayOrder;
        UpdatedAt = now;
        Raise(new CategoryUpdatedDomainEvent(Id, Name, Slug.Value));
    }

    public void Move(long? newParentId, int displayOrder, DateTimeOffset now)
    {
        EnsureNotDeleted();
        ValidateParent(newParentId);
        ValidateDisplayOrder(displayOrder);
        if (newParentId == Id)
            throw new DomainException(
                "CATEGORY_PARENT_INVALID",
                "A category cannot be its own parent.");

        ParentId = newParentId;
        DisplayOrder = displayOrder;
        UpdatedAt = now;
        Raise(new CategoryMovedDomainEvent(Id, ParentId, DisplayOrder));
    }

    /// <summary>
    /// Validates update details without changing an aggregate or raising an event.
    /// Application services use this before loading a tracked instance so malformed
    /// commands cannot cause outbound I/O or partial in-memory mutation.
    /// </summary>
    public static void ValidateDetails(
        string name, string slug, string? description, int displayOrder) =>
        _ = ValidateDetailsCore(name, slug, description, displayOrder);

    public static string NormalizeNameForComparison(string name) =>
        name.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();

    /// <summary>Validates move-specific values without changing an aggregate.</summary>
    public static void ValidateMove(long? newParentId, int displayOrder)
    {
        ValidateParent(newParentId);
        ValidateDisplayOrder(displayOrder);
    }

    public void SetStatus(
        CategoryStatus targetStatus,
        bool parentIsActive,
        bool hasDependencies,
        DateTimeOffset now)
    {
        EnsureNotDeleted();
        if (!Enum.IsDefined(targetStatus))
            throw new DomainException(
                "CATEGORY_STATUS_INVALID",
                "Category status is invalid.");

        switch (targetStatus)
        {
            case CategoryStatus.Active:
                SetActive(parentIsActive, now);
                break;
            case CategoryStatus.Inactive:
                SetInactive(now);
                break;
            case CategoryStatus.Deleted:
                SetDeleted(hasDependencies, now);
                break;
        }
    }

    private void SetActive(bool parentIsActive, DateTimeOffset now)
    {
        EnsureNotDeleted();
        if (Status == CategoryStatus.Active) return;
        if (ParentId.HasValue && !parentIsActive)
            throw new DomainException(
                "CATEGORY_PARENT_INACTIVE",
                "A category cannot be activated beneath an inactive parent.");

        Status = CategoryStatus.Active;
        UpdatedAt = now;
        Raise(new CategoryActivatedDomainEvent(Id));
    }

    private void SetInactive(DateTimeOffset now)
    {
        EnsureNotDeleted();
        if (Status != CategoryStatus.Active) return;

        Status = CategoryStatus.Inactive;
        UpdatedAt = now;
        Raise(new CategoryDeactivatedDomainEvent(Id));
    }
    private void SetDeleted(bool hasDependencies, DateTimeOffset now)
    {
        EnsureNotDeleted();
        if (hasDependencies)
            throw new DomainException(
                "CATEGORY_HAS_CHILDREN",
                "امکان حذف دسته‌بندی دارای کالا یا زیردسته وجود ندارد. ابتدا وابستگی‌ها را منتقل کنید");

        Status = CategoryStatus.Deleted;
        DeletedAt = now;
        UpdatedAt = now;
        Raise(new CategoryDeletedDomainEvent(Id, now));
    }
    private void EnsureNotDeleted()
    {
        if (Status == CategoryStatus.Deleted)
            throw new DomainException(
                "CATEGORY_DELETED",
                "A deleted category cannot be changed.");
    }
    private static (string Name, CategorySlug Slug, string? Description) ValidateDetailsCore(
        string name,
        string slug,
        string? description,
        int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(
                "CATEGORY_NAME_REQUIRED",
                "نام دسته‌بندی اجباری است");

        var normalizedName = name.Trim();
        if (normalizedName.Length > 60)
            throw new DomainException(
                "CATEGORY_NAME_TOO_LONG",
                "Category name cannot exceed 60 characters or contain unsupported characters");
        if (!HasOnlySupportedNameCharacters(normalizedName))
            throw new DomainException(
                "CATEGORY_NAME_UNSUPPORTED",
                "Category name cannot exceed 60 characters or contain unsupported characters");

        ValidateDisplayOrder(displayOrder);
        return (normalizedName, CategorySlug.Create(slug), NormalizeDescription(description));
    }

    private static bool HasOnlySupportedNameCharacters(string name)
    {
        foreach (var rune in name.EnumerateRunes())
        {
            if (!IsSupportedNameRune(rune)) return false;
        }

        return true;
    }

    private static bool IsSupportedNameRune(Rune rune)
    {
        var value = rune.Value;
        if (value is ' ' or '-' or '_' or 0x200C) return true;
        if (value is >= '0' and <= '9'
            or >= 0x0660 and <= 0x0669
            or >= 0x06F0 and <= 0x06F9)
            return true;

        var category = Rune.GetUnicodeCategory(rune);
        var isLetterOrMark = category is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark;
        if (!isLetterOrMark) return false;

        return value is >= 0x0041 and <= 0x005A
            or >= 0x0061 and <= 0x007A
            or >= 0x00C0 and <= 0x024F
            or >= 0x0600 and <= 0x06FF
            or >= 0x0750 and <= 0x077F
            or >= 0x0870 and <= 0x089F
            or >= 0x08A0 and <= 0x08FF
            or >= 0xFB50 and <= 0xFDFF
            or >= 0xFE70 and <= 0xFEFF;
    }
    private static string? NormalizeDescription(string? description)
    {
        if (description is null) return null;

        var normalized = description.Trim();
        if (normalized.Length > 1000)
            throw new DomainException(
                "CATEGORY_DESCRIPTION_TOO_LONG",
                "Category description cannot exceed 1000 characters.");

        return normalized.Length == 0 ? null : normalized;
    }

    private static void ValidateDisplayOrder(int displayOrder)
    {
        if (displayOrder < 0)
            throw new DomainException(
                "CATEGORY_DISPLAY_ORDER_INVALID",
                "Category display order cannot be negative.");
    }

    private static void ValidateParent(long? parentId)
    {
        if (parentId.HasValue && parentId.Value <= 0)
            throw new DomainException(
                "CATEGORY_PARENT_INVALID",
                "Category parent must be a positive identifier.");
    }
}
