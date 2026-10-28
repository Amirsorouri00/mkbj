using EwaysVNext.Database;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using EwaysVNext.Modules.Location.Application.Ports.Outbound;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Location.Adapter.Outbound.Repositories;

internal sealed class AddressRepository(EwaysVNextDbContext db) : EfCoreRepository<Address, long>(db),
    IAddressRepository
{
    private readonly EwaysVNextDbContext _db = db;
    
    public async Task<List<Address>?> GetByIdsAsync(List<long> ids,CancellationToken ct)
    {
        return await _db.Addresses.AsNoTracking().Where(a=>ids.Contains(a.Id)).ToListAsync(ct);
    }

    public async Task<List<Address>> GetTrackedByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
        => await _db.Addresses.Where(a => ids.Contains(a.Id)).ToListAsync(ct);
}
