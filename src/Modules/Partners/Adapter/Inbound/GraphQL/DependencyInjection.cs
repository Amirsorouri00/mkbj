using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Partners.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    public static IServiceCollection AddPartnersGraphQL(this IServiceCollection services)
    {
        services
            .AddGraphQLBootstrap()
            .AddType<PartnerAdminGraphQLQueries>()
            .AddType<PartnerAdminGraphQLMutations>();

        return services;
    }
}
