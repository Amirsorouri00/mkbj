using EwaysVNext.Modules.Location.Application.Domain.Locations;

namespace EwaysVNext.Modules.Location.Adapter.Inbound.GraphQL.Locations;

public sealed class ProvinceType : ObjectType<Province>
{
    protected override void Configure(IObjectTypeDescriptor<Province> descriptor)
    {
        descriptor.Ignore(p => p.RowVersion);
        descriptor.Ignore(p => p.DomainEvents);

        // The `provinces` query never loads cities, so exposing this field would always
        // resolve to an empty list. Cities are reached via `citiesByProvinceId` instead.
        descriptor.Ignore(p => p.Cities);
    }
}
