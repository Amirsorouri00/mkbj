using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.AttributeBlueprints;

public sealed class AttributeBlueprintType : ObjectType<AttributeBlueprintSnapshot>
{
    protected override void Configure(IObjectTypeDescriptor<AttributeBlueprintSnapshot> descriptor)
    {
        descriptor.Field(blueprint => blueprint.Id).Type<NonNullType<IdType>>();
        descriptor.Field(blueprint => blueprint.ScopeCategoryId).Type<IdType>();
        descriptor.Field(blueprint => blueprint.UnitId).Type<IdType>();
    }
}

public sealed class AttributeBlueprintOptionType : ObjectType<AttributeBlueprintOptionSnapshot>
{
    protected override void Configure(IObjectTypeDescriptor<AttributeBlueprintOptionSnapshot> descriptor)
    {
        descriptor.Field(option => option.Id).Type<NonNullType<IdType>>();
        descriptor.Field(option => option.MeasurementUnitId).Type<IdType>();
    }
}

public sealed class MeasurementUnitType : ObjectType<MeasurementUnitSnapshot>
{
    protected override void Configure(IObjectTypeDescriptor<MeasurementUnitSnapshot> descriptor)
        => descriptor.Field(unit => unit.Id).Type<NonNullType<IdType>>();
}
