using EwaysVNext.Modules.Sales.Adapter.Inbound.GraphQL.Shop;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    /// <summary>Sales adds its resolvers to the one shared schema.</summary>
    public static IServiceCollection AddSalesGraphQL(this IServiceCollection services)
    {
        services
            .AddGraphQLBootstrap()
            .AddType<ShopCartQueries>()
            .AddType<ShopCartMutations>();

        return services;
    }
}
