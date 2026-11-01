using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.AttributeBlueprints;

public sealed record CreateAttributeBlueprintInput(
    string Name,
    AttributeBlueprintDataType DataType,
    IReadOnlyList<AttributeBlueprintOptionInput>? Options = null);

public sealed record AttributeBlueprintOptionInput(
    string Value,
    [property: GraphQLType(typeof(IdType))] long? MeasurementUnitId);

public sealed record UpdateAttributeBlueprintInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long AttributeBlueprintId,
    string Name,
    AttributeBlueprintDataType DataType,
    bool IsActive,
    IReadOnlyList<AttributeBlueprintOptionUpdateInput> Options);

public sealed record AttributeBlueprintOptionUpdateInput(
    [property: GraphQLType(typeof(IdType))] long? OptionId,
    string Value,
    [property: GraphQLType(typeof(IdType))] long? MeasurementUnitId);

public sealed record SetAttributeBlueprintScopeInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long AttributeBlueprintId,
    AttributeScope Scope,
    [property: GraphQLType(typeof(IdType))] long? CategoryId);

public sealed record SetAttributeBlueprintAliasesInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long AttributeBlueprintId,
    IReadOnlyList<string> Aliases);

public sealed record SetAttributeBlueprintUnitInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long AttributeBlueprintId,
    [property: GraphQLType(typeof(IdType))] long? UnitId);

public sealed record AddAttributeBlueprintOptionInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long AttributeBlueprintId,
    string Value,
    [property: GraphQLType(typeof(IdType))] long? MeasurementUnitId);

public sealed record UpdateAttributeBlueprintOptionInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long AttributeBlueprintId,
    [property: GraphQLType(typeof(NonNullType<IdType>))] long OptionId,
    string Value,
    [property: GraphQLType(typeof(IdType))] long? MeasurementUnitId);
