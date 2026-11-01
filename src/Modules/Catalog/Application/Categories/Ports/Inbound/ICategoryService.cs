using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Catalog.Application.Categories.Commands;
using EwaysVNext.Modules.Catalog.Contracts.Categories;

namespace EwaysVNext.Modules.Catalog.Application.Categories.Ports.Inbound;

public interface ICategoryService
{
    Task<Result<long>> CreateAsync(CreateCategoryCommand command, CancellationToken ct = default);
    Task<Result<CategoryAdminDto>> UpdateAsync(
        UpdateCategoryCommand command, CancellationToken ct = default);
    Task<Result<CategoryAdminDto>> SetStatusAsync(
        SetCategoryStatusCommand command, CancellationToken ct = default);
    Task<Result> MoveAsync(MoveCategoryCommand command, CancellationToken ct = default);

    /// <summary>
    /// Replaces the category's aliases — the names a supplier's feed uses for it, which the
    /// canonical-key normalizer deliberately will not guess at.
    /// </summary>
    Task<Result> SetAliasesAsync(long categoryId, IReadOnlyCollection<string> aliases,
        CancellationToken ct = default);
    Task<Result> DeactivateManyAsync(BulkDeactivateCategoriesCommand command,
        CancellationToken ct = default);
    Task<Result> DeleteAsync(DeleteCategoryCommand command, CancellationToken ct = default);
    Task<Result> AssignProductAsync(AssignCategoryProductCommand command, CancellationToken ct = default);
    Task<Result> UnassignProductAsync(UnassignCategoryProductCommand command, CancellationToken ct = default);
}
