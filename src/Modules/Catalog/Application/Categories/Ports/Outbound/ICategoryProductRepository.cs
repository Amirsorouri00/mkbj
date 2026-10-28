using EwaysVNext.Modules.Catalog.Application.Categories.Domain;

namespace EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;

public interface ICategoryProductRepository
{
    Task<bool> ExistsAsync(long categoryId, long productId, CancellationToken ct = default);
    Task<bool> HasAnyForCategoryAsync(long categoryId, CancellationToken ct = default);
    Task<AssociationMutationOutcome> AddIfAbsentAsync(
        CategoryProduct assignment, CancellationToken ct = default);
    Task<AssociationMutationOutcome> RemoveIfPresentAsync(
        long categoryId, long productId, CancellationToken ct = default);
    Task AddAsync(CategoryProduct assignment, CancellationToken token);
}

public enum AssociationMutationOutcome
{
    Applied,
    AlreadyInDesiredState,
    PersistenceConflict
}
