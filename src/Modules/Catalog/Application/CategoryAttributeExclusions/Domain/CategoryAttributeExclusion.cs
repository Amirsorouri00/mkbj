using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain;

public sealed class CategoryAttributeExclusion : AggregateRoot<long>
{
    public long CategoryId { get; private set; }

    public long SourceAssignmentId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private CategoryAttributeExclusion() { }

    public static CategoryAttributeExclusion Create(
        long id,
        long categoryId,
        long sourceAssignmentId,
        DateTimeOffset now)
    {
        if (id <= 0)
            throw new DomainException("CATEGORY_ATTRIBUTE_EXCLUSION_ID_INVALID", "Category attribute exclusion ID must be positive.");
        if (categoryId <= 0)
            throw new DomainException("CATEGORY_ATTRIBUTE_EXCLUSION_CATEGORY_INVALID", "Category ID must be positive.");
        if (sourceAssignmentId <= 0)
            throw new DomainException("CATEGORY_ATTRIBUTE_EXCLUSION_SOURCE_INVALID", "Source assignment ID must be positive.");

        var exclusion = new CategoryAttributeExclusion
        {
            Id = id,
            CategoryId = categoryId,
            SourceAssignmentId = sourceAssignmentId,
            CreatedAt = now
        };

        exclusion.Raise(new CategoryAttributeExcludedDomainEvent(exclusion.Id, categoryId, sourceAssignmentId));
        return exclusion;
    }
}
