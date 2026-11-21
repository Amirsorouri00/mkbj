using Mkbj.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using Mkbj.Modules.Catalog.QueryApplication.Abstractions.AttributeBlueprints;
using Mkbj.Modules.Catalog.QueryApplication.AttributeBlueprints;

namespace Mkbj.Modules.Catalog.Application.AttributeBlueprints.Services;

public sealed class MeasurementUnitQueryService(IMeasurementUnitRepository repository) : IMeasurementUnitQueryService
{
    public Task<IReadOnlyList<MeasurementUnitSnapshot>> ListAsync(CancellationToken ct = default)
        => repository.ListSnapshotsAsync(ct);
}
