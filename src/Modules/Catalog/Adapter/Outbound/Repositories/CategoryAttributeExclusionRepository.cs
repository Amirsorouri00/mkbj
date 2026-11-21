using Microsoft.EntityFrameworkCore;
using Mkbj.Database;
using Mkbj.Persistence;

namespace Mkbj.Modules.Catalog.Adapter.Outbound.Repositories;

internal sealed class CategoryAttributeExclusionRepository
    : EfCoreRepository<Application.CategoryAttributeExclusions.Domain.CategoryAttributeExclusion, long>,
        Application.CategoryAttributeExclusions.Ports.Outbound.ICategoryAttributeExclusionRepository
{
    private readonly MkbjDbContext _db;
    public CategoryAttributeExclusionRepository(MkbjDbContext db) : base(db) => _db = db;

    public async Task<Application.CategoryAttributeExclusions.Domain.CategoryAttributeExclusion?> GetByCategoryAndSourceAsync(
        long categoryId, long sourceAssignmentId, CancellationToken ct = default)
        => await _db.CategoryAttributeExclusions.FirstOrDefaultAsync(
            e => e.CategoryId == categoryId && e.SourceAssignmentId == sourceAssignmentId, ct);

    public async Task<IReadOnlyList<Application.CategoryAttributeExclusions.Domain.CategoryAttributeExclusion>> ListByCategoryAsync(
        long categoryId, CancellationToken ct = default)
        => await _db.CategoryAttributeExclusions
            .Where(e => e.CategoryId == categoryId)
            .ToListAsync(ct);

    public Task RemoveAsync(Application.CategoryAttributeExclusions.Domain.CategoryAttributeExclusion exclusion, CancellationToken ct = default)
    {
        _db.CategoryAttributeExclusions.Remove(exclusion);
        return Task.CompletedTask;
    }
}
