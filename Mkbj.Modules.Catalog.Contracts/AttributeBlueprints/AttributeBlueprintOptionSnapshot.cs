namespace Mkbj.Modules.Catalog.QueryApplication.AttributeBlueprints;

public sealed record AttributeBlueprintOptionSnapshot(
    long Id,
    string Value,
    long? MeasurementUnitId);
