using Mkbj.Modules.Catalog.QueryApplication.AttributeBlueprints;

namespace Mkbj.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;

public interface IMeasurementUnitRepository
{
    Task<bool> ExistsAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<MeasurementUnitSnapshot>> ListSnapshotsAsync(CancellationToken ct = default);
}
