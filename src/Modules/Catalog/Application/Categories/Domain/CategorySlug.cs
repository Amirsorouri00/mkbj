using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.Categories.Domain;

public sealed class CategorySlug : ValueObject
{
    public string Value { get; }

    private CategorySlug(string value) => Value = value;

    public static CategorySlug Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new DomainException(
                "CATEGORY_SLUG_REQUIRED",
                "Category slug is required.");

        var normalized = input.Trim().ToLowerInvariant();
        if (normalized.Length > 200)
            throw new DomainException(
                "CATEGORY_SLUG_TOO_LONG",
                "Category slug cannot exceed 200 characters.");

        return new CategorySlug(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
