using Microsoft.EntityFrameworkCore;
using EwaysVNext.Database;
using EwaysVNext.Persistence;
using EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Repositories;

internal sealed class CategoryAttributeAssignmentRepository
    : EfCoreRepository<Application.CategoryAttributeAssignments.Domain.CategoryAttributeAssignment, long>,
        Application.CategoryAttributeAssignments.Ports.Outbound.ICategoryAttributeAssignmentRepository
{
    private readonly EwaysVNextDbContext _db;
    public CategoryAttributeAssignmentRepository(EwaysVNextDbContext db) : base(db) => _db = db;

    public new async Task<Application.CategoryAttributeAssignments.Domain.CategoryAttributeAssignment?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.CategoryAttributeAssignments.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<Application.CategoryAttributeAssignments.Domain.CategoryAttributeAssignment?> GetActiveByCategoryAndAttributeAsync(
        long categoryId, long attributeBlueprintId, CancellationToken ct = default)
        => await _db.CategoryAttributeAssignments.FirstOrDefaultAsync(
            a => a.CategoryId == categoryId && a.AttributeBlueprintId == attributeBlueprintId && a.RemovedAt == null, ct);

    public async Task<IReadOnlyList<Application.CategoryAttributeAssignments.Domain.CategoryAttributeAssignment>> ListByCategoryAsync(
        long categoryId, CancellationToken ct = default)
        => await _db.CategoryAttributeAssignments
            .Where(a => a.CategoryId == categoryId && a.RemovedAt == null)
            .OrderBy(a => a.TitleOrder)
            .ToListAsync(ct);

    public async Task<int> CountActivePrimaryInCategoriesAsync(
        IReadOnlyList<long> categoryIds, long? excludingAssignmentId = null, CancellationToken ct = default)
        => await _db.CategoryAttributeAssignments
            .Where(a => categoryIds.Contains(a.CategoryId) && a.RemovedAt == null && a.IsPrimary
                && (excludingAssignmentId == null || a.Id != excludingAssignmentId))
            .CountAsync(ct);

    public async Task<IReadOnlyList<CategoryAttributeAssignmentAdminRow>> GetAdminRowsByCategoryAsync(
        long categoryId, CancellationToken ct = default)
        => await _db.CategoryAttributeAssignments
            .Where(a => a.CategoryId == categoryId && a.RemovedAt == null)
            .OrderByDescending(a => a.IsVariantGenerating)
            .ThenByDescending(a => a.IsPrimary)
            .ThenBy(a => a.TitleOrder ?? int.MaxValue)
            .ThenBy(a => a.Id)
            .Select(a => new CategoryAttributeAssignmentAdminRow(
                a.Id, a.CategoryId, a.AttributeBlueprintId,
                a.IsRequired, a.IsFilterable, a.IsSearchable, a.IsInTitle, a.TitleOrder,
                a.IsPrimary, a.IsVariantGenerating,
                a.IsVariantGenerating ? CategoryAttributePriorityGroup.VariantGenerating
                    : a.IsPrimary ? CategoryAttributePriorityGroup.Primary
                    : CategoryAttributePriorityGroup.Standard))
            .ToListAsync(ct);
}
