using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Shop;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerGraphQL(this IServiceCollection services)
    {
        services
            .AddGraphQLBootstrap()
            .AddType<ShopMeQueries>()
            .AddType<ShopProfileMutations>()
            .AddType<Admin.CustomerAdminGraphQLQueries>()
            .AddType<Admin.CustomerAdminGraphQLMutations>();

        return services;
    }
}
