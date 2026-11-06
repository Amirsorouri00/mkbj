using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    /// <summary>
    /// Sales adds its resolvers to the one shared schema. Types arrive in phase 5; the bootstrap
    /// call is here from the start so the module is wired the same way as every other.
    /// </summary>
    public static IServiceCollection AddSalesGraphQL(this IServiceCollection services)
    {
        services.AddGraphQLBootstrap();
        return services;
    }
}
