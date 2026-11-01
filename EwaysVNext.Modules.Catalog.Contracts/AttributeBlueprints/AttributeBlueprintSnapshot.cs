namespace EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;

public sealed record AttributeBlueprintSnapshot(
    long Id,
    string Name,
    string CanonicalKey,
    string DataType,
    string Status,
    string Scope,
    long? ScopeCategoryId,
    IReadOnlyList<string> Aliases,
    long? UnitId,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<AttributeBlueprintOptionSnapshot> Options);
