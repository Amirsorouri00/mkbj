using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Pricing.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    public static IServiceCollection AddPricingGraphQL(this IServiceCollection services)
    {
        services
            .AddGraphQLBootstrap()
            .AddType<CurrencyGraphQLQueries>()
            .AddType<PricingAdminGraphQLQueries>()
            .AddType<PricingAdminGraphQLMutations>();

        return services;
    }
}
