using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using System.Globalization;
using System.Text;

namespace EwaysVNext.Modules.Catalog.Application.Categories.Domain;

public sealed class Category : AggregateRoot<long>
{
    private const int StableHashCode = 1;

    /// <summary>
    /// How many names one category may be known by. Generous for a real catalogue, bounded
    /// because the list is loaded with every category and an unbounded one would be an import
    /// bug nobody noticed until the page stopped loading.
    /// </summary>
    public const int MaxAliases = 20;

    /// <summary>
    /// The only punctuation refused outright, because it reads as hierarchy wherever a category
    /// path is displayed.
    /// <para>
    /// A denylist rather than an allowlist: every script punctuates differently — a Japanese
    /// name separates words with U+30FB, as in 家電・キッチン — and enumerating what each one
    /// needs produces a list that is wrong until someone complains.
    /// </para>
    /// </summary>
    private static readonly HashSet<int> PathLikePunctuation = ['/', '\\'];

    private readonly List<Category> _children = [];
    private readonly List<string> _aliases = [];

    public long? ParentId { get; private set; }
    public Category? Parent { get; private set; }
    public IReadOnlyCollection<Category> Children => _children;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary>
    /// Derived from <see cref="Name"/>, never set directly. It is what an import matches on, so
    /// letting an admin edit it independently would mean a rename silently stopped matching the
    /// feed that has been filling the category for months.
    /// </summary>
    public string CanonicalKey { get; private set; } = string.Empty;

    /// <summary>
    /// Other canonical keys that should resolve to this category — the names suppliers actually
    /// use. This is where "Kitchen and Dining" is matched to "Kitchen &amp; Dining", which the
    /// normalizer deliberately will not guess at.
    /// </summary>
    public IReadOnlyList<string> Aliases => _aliases;
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
            CanonicalKey = Common.CanonicalKey.From(details.Name),
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
        CanonicalKey = Common.CanonicalKey.From(details.Name);
        Slug = details.Slug;
        Description = details.Description;
        DisplayOrder = displayOrder;
        UpdatedAt = now;
        Raise(new CategoryUpdatedDomainEvent(Id, Name, Slug.Value));
    }

    /// <summary>
    /// Replaces the alias list. Each entry is canonicalised, duplicates collapse, and an alias
    /// equal to this category's own key is dropped — it would match nothing the key does not
    /// already match.
    /// <para>
    /// An entry with no letters or digits is skipped rather than fatal. These arrive from an
    /// import's alias column, which holds whatever the supplier put there, and one unusable
    /// value must not reject the rest.
    /// </para>
    /// </summary>
    public void SetAliases(IEnumerable<string> aliases, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        EnsureNotDeleted();

        var canonical = new List<string>();

        foreach (var alias in aliases)
        {
            if (!Common.CanonicalKey.TryFrom(alias, out var key))
                continue;

            if (string.Equals(key, CanonicalKey, StringComparison.Ordinal) || canonical.Contains(key, StringComparer.Ordinal))
                continue;

            canonical.Add(key);
        }

        if (canonical.Count > MaxAliases)
        {
            throw new DomainException(
                "CATEGORY_ALIASES_TOO_MANY",
                $"A category may have at most {MaxAliases} aliases; {canonical.Count} were given.");
        }

        _aliases.Clear();
        _aliases.AddRange(canonical);
        UpdatedAt = now;
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

    /// <summary>
    /// Marks this category as changed because one of its children did.
    /// <para>
    /// This is what replaced the serializable transaction (ADR-0017 D8): a write whose validity
    /// depends on the parent's state — a new child, a move into this parent, a child becoming
    /// active — also touches the parent, so the parent's <c>xmin</c> changes and a concurrent
    /// writer that read the old parent loses on its concurrency check instead of both succeeding
    /// against a stale view.
    /// </para>
    /// </summary>
    public void RecordChildChanged(DateTimeOffset now) => UpdatedAt = now;

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
                "A category name is required.");

        var normalizedName = name.Trim();
        if (normalizedName.Length > 60)
            throw new DomainException(
                "CATEGORY_NAME_TOO_LONG",
                "A category name cannot exceed 60 characters.");
        if (!HasOnlySupportedNameCharacters(normalizedName))
            throw new DomainException(
                "CATEGORY_NAME_UNSUPPORTED",
                "A category name can contain letters, digits, spaces and basic punctuation.");

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

    /// <summary>
    /// A category name is display text, so what needs constraining is control characters and
    /// length — not which script it is written in.
    /// <para>
    /// This used to be an allowlist of Latin and Arabic/Persian ranges, inherited from the
    /// Iran-market codebase. For a seller trading internationally that silently refuses a
    /// Chinese, Japanese, Korean or Thai category name, and it also refused ordinary English
    /// punctuation: "Kitchen &amp; Dining" could not be created at all.
    /// </para>
    /// <para>
    /// Separators and format characters are allowed because scripts need them — a zero-width
    /// non-joiner carries meaning in Persian, and ideographic space in Japanese.
    /// </para>
    /// </summary>
    private static bool IsSupportedNameRune(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);

        if (PathLikePunctuation.Contains(rune.Value))
            return false;

        // '&' and '+' are OtherPunctuation and MathSymbol respectively, and both belong in a
        // name. Other symbol categories stay out, which is what keeps emoji from category names.
        if (category is UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol)
            return true;

        // Letters, marks and digits from any script, plus spacing. Symbols are excluded, which
        // is what keeps emoji out — a decision the original rule made and worth keeping; it was
        // only the script allowlist that was wrong.
        return category is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark or
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.LetterNumber or
            UnicodeCategory.OtherNumber or
            UnicodeCategory.SpaceSeparator or
            UnicodeCategory.Format or
            UnicodeCategory.ConnectorPunctuation or
            UnicodeCategory.DashPunctuation or
            UnicodeCategory.OpenPunctuation or
            UnicodeCategory.ClosePunctuation or
            UnicodeCategory.InitialQuotePunctuation or
            UnicodeCategory.FinalQuotePunctuation;
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
