using EwaysVNext.Core.Application;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

/// <summary>
/// Everything an admin does to a product. Every method takes the actor explicitly — there is no
/// ambient current-user here, because each one writes an audit entry naming who did it.
/// </summary>
public interface ICatalogAdminService
{
    Task<Result<long>> CreateProductAsync(CreateProductCommand command, AdminActor actor,
        CancellationToken ct = default);

    Task<Result> UpdateProductAsync(UpdateProductCommand command, AdminActor actor,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces the product's secondary categories. The primary category is set through
    /// <see cref="UpdateProductAsync"/> and is not one of these.
    /// </summary>
    Task<Result> SetSecondaryCategoriesAsync(long productId, IReadOnlyList<long> categoryIds,
        AdminActor actor, CancellationToken ct = default);

    Task<Result<long>> AddVariantAsync(AddVariantCommand command, AdminActor actor,
        CancellationToken ct = default);

    Task<Result> AddIdentifierAsync(long productId, IdentifierInput identifier, AdminActor actor,
        CancellationToken ct = default);

    Task<Result> RemoveIdentifierAsync(long productId, long identifierId, AdminActor actor,
        CancellationToken ct = default);

    Task<Result> SetProductActiveAsync(long productId, bool active, AdminActor actor,
        CancellationToken ct = default);

    Task<Result> SetVariantActiveAsync(long productId, long variantId, bool active,
        AdminActor actor, CancellationToken ct = default);
}
