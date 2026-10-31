using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Settings.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    public static IServiceCollection AddSettingsGraphQL(this IServiceCollection services)
    {
        services
            .AddGraphQLBootstrap()
            .AddType<SettingsAdminGraphQLQueries>()
            .AddType<SettingsAdminGraphQLMutations>();

        return services;
    }
}
