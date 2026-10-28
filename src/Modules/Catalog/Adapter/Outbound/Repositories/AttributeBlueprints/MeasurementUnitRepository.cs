using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Repositories.AttributeBlueprints;

internal sealed class MeasurementUnitRepository(EwaysVNextDbContext db) : IMeasurementUnitRepository
{
    public Task<bool> ExistsAsync(long id, CancellationToken ct = default)
        => db.MeasurementUnits.AnyAsync(unit => unit.Id == id, ct);

    public async Task<IReadOnlyList<MeasurementUnitSnapshot>> ListSnapshotsAsync(CancellationToken ct = default)
        => await db.MeasurementUnits
            .OrderBy(unit => unit.Name)
            .ThenBy(unit => unit.Id)
            .Select(unit => new MeasurementUnitSnapshot(unit.Id, unit.Name, unit.Symbol))
            .ToListAsync(ct);
}
