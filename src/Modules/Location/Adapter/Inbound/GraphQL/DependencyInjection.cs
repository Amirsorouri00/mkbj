using EwaysVNext.Modules.Location.Adapter.Inbound.GraphQL.Locations;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Location.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    public static IServiceCollection AddLocationGraphQL(this IServiceCollection builder)
    {
        builder
            .AddGraphQLBootstrap()
            .AddType<LocationQueries>()
            .AddType<ProvinceType>()
            .AddType<CityType>()
            .AddType<DistrictType>();

        return builder;
    }
}
