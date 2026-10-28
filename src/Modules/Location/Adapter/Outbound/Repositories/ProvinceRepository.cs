using EwaysVNext.Database;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace EwaysVNext.Modules.Location.Adapter.Outbound.Repositories;
internal sealed class ProvinceRepository(EwaysVNextDbContext db) : EfCoreRepository<Province, int>(db),
    Application.Ports.Outbound.IProvinceRepository
{
    private readonly EwaysVNextDbContext _db = db;
    private IQueryable<Province> _baseQuery => _db.Provinces.AsNoTracking().Where(x => x.IsActive);
    public async Task<IList<Province>> GetAllProvincesAsync(Expression<Func<Province, bool>>? predicate = null, CancellationToken ct = default) =>
        predicate != null ? await _baseQuery.Where(predicate).ToListAsync(ct) :
        await _baseQuery.ToListAsync(ct);
    public async Task<Province?> GetProvinceAsync(Expression<Func<Province, bool>> predicate,
    bool includeCities = true,
    CancellationToken ct = default)
    {
        var query = _baseQuery;

        if (includeCities)
            query = query.Include(x => x.Cities)
                    .ThenInclude(c => c.Districts);

        return await query.FirstOrDefaultAsync(predicate, ct);
    }
}
