namespace EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;

public sealed record AttributeBlueprintSnapshot(
    long Id,
    string Name,
    string DataType,
    string Status,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<AttributeBlueprintOptionSnapshot> Options);
