using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;

public sealed class AttributeBlueprintOption : Entity<long>
{
    public long AttributeBlueprintId { get; private set; }

    public string Value { get; private set; } = null!;

    public long? MeasurementUnitId { get; private set; }

    private AttributeBlueprintOption() { }

    internal AttributeBlueprintOption(long id, long attributeBlueprintId, string value, long? measurementUnitId)
    {
        Id = id;
        AttributeBlueprintId = attributeBlueprintId;
        Value = value;
        MeasurementUnitId = measurementUnitId;
    }

    internal void Update(string value, long? measurementUnitId)
    {
        Value = value;
        MeasurementUnitId = measurementUnitId;
    }
}
