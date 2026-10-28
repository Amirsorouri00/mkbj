using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.QueryApplication.Abstractions.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Services;

public sealed class MeasurementUnitQueryService(IMeasurementUnitRepository repository) : IMeasurementUnitQueryService
{
    public Task<IReadOnlyList<MeasurementUnitSnapshot>> ListAsync(CancellationToken ct = default)
        => repository.ListSnapshotsAsync(ct);
}
