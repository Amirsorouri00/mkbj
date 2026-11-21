using Mkbj.Modules.Sales.Adapter.Inbound.GraphQL.Admin;
using Mkbj.Modules.Sales.Adapter.Inbound.GraphQL.Shop;
using Mkbj.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Sales.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    /// <summary>Sales adds its resolvers to the one shared schema.</summary>
    public static IServiceCollection AddSalesGraphQL(this IServiceCollection services)
    {
        services
            .AddGraphQLBootstrap()
            .AddType<ShopCartQueries>()
            .AddType<ShopCartMutations>()
            .AddType<ShopOrderQueriesGraph>()
            .AddType<ShopCheckoutMutations>()
            .AddType<ShopCartLineType>()
            .AddType<ShopCartChangeType>()
            .AddType<ShopOrderType>()
            .AddType<ShopOrderLineType>()
            .AddType<ShopDocumentType>()
            .AddType<ShopOrderPageType>()
            .AddType<AdminOrderQueries>()
            .AddType<AdminOrderMutations>();

        return services;
    }
}
