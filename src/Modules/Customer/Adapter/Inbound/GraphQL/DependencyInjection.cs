using Mkbj.Modules.Customer.Adapter.Inbound.GraphQL.Shop;
using Mkbj.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Customer.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerGraphQL(this IServiceCollection services)
    {
        services
            .AddGraphQLBootstrap()
            .AddType<ShopMeQueries>()
            .AddType<ShopProfileMutations>()
            .AddType<ShopCustomerProfileType>()
            .AddType<Admin.CustomerAdminGraphQLQueries>()
            .AddType<Admin.CustomerAdminGraphQLMutations>();

        return services;
    }
}
