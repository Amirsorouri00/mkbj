namespace Mkbj.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;

public sealed record UpdateAttributeBlueprintOptionInput(
    long? OptionId,
    string Value,
    long? MeasurementUnitId);
