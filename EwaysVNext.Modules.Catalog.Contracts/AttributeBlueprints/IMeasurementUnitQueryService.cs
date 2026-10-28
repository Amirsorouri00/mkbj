using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Abstractions.AttributeBlueprints;

public interface IMeasurementUnitQueryService
{
    Task<IReadOnlyList<MeasurementUnitSnapshot>> ListAsync(CancellationToken ct = default);
}
