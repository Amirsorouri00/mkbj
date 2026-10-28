using AuthorizationService.Endpoints.Permissions;
using AuthorizationService.Endpoints.Roles;
using AuthorizationService.Endpoints.Scopes;
using AuthorizationService.Endpoints.UserRoleScopes;
using AuthorizationService.Implementation;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthorizationService.Endpoints;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthorizationSystem(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthorizationServices(configuration);
        services.AddAuthorizationGraphQL();

        return services;
    }

    public static IServiceCollection AddAuthorizationGraphQL(this IServiceCollection services)
    {
        services.AddGraphQLBootstrap()
            .AddType<PermissionQueries>()
            .AddType<PermissionMutations>()
            .AddType<RoleQueries>()
            .AddType<RoleMutations>()
            .AddType<ScopeQueries>()
            .AddType<ScopeMutations>()
            .AddType<UserRoleScopeQueries>()
            .AddType<UserRoleScopeMutations>();

        return services;
    }
}
