using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Admin.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    public static IServiceCollection AddAdminGraphQL(this IServiceCollection services)
    {
        services
            .AddGraphQLBootstrap()
            .AddType<AdminCompositionQueries>();

        return services;
    }
}
