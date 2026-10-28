using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;

public interface IMeasurementUnitRepository
{
    Task<bool> ExistsAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<MeasurementUnitSnapshot>> ListSnapshotsAsync(CancellationToken ct = default);
}
