using EwaysVNext.Modules.Location.Application.Domain.Locations;

namespace EwaysVNext.Modules.Location.Adapter.Inbound.GraphQL.Locations;

public sealed class CityType : ObjectType<City>
{
    protected override void Configure(IObjectTypeDescriptor<City> descriptor)
    {
    }
}
