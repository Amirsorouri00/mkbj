using Mkbj.Modules.Catalog.QueryApplication.AttributeBlueprints;

namespace Mkbj.Modules.Catalog.QueryApplication.Abstractions.AttributeBlueprints;

public interface IMeasurementUnitQueryService
{
    Task<IReadOnlyList<MeasurementUnitSnapshot>> ListAsync(CancellationToken ct = default);
}
