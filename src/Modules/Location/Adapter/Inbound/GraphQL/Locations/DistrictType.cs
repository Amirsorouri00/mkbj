using EwaysVNext.Modules.Location.Application.Domain.Locations;

namespace EwaysVNext.Modules.Location.Adapter.Inbound.GraphQL.Locations;

public sealed class DistrictType : ObjectType<District>
{
    protected override void Configure(IObjectTypeDescriptor<District> descriptor)
    {
    }
}
