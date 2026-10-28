using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.Categories.Domain;

public sealed class CategoryProduct : IEquatable<CategoryProduct>
{
    public long CategoryId { get; private set; }
    public long ProductId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public Guid? AssignedByUserId { get; private set; }

    private CategoryProduct() { }

    public bool Equals(CategoryProduct? other) =>
        other is not null
        && CategoryId == other.CategoryId
        && ProductId == other.ProductId;

    public override bool Equals(object? obj) =>
        obj is CategoryProduct other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CategoryId, ProductId);

    public static bool operator ==(CategoryProduct? left, CategoryProduct? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(CategoryProduct? left, CategoryProduct? right) =>
        !(left == right);

    public static CategoryProduct Create(
        long categoryId,
        long productId,
        DateTimeOffset assignedAt,
        Guid? assignedByUserId = null)
    {
        if (categoryId <= 0 || productId <= 0)
            throw new DomainException(
                "CATEGORY_PRODUCT_INVALID",
                "A valid category and product are required for an assignment.");

        return new CategoryProduct
        {
            CategoryId = categoryId,
            ProductId = productId,
            AssignedAt = assignedAt,
            AssignedByUserId = assignedByUserId
        };
    }
}
