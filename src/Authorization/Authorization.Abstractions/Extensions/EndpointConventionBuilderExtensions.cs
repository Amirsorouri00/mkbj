using AuthorizationService.Abstractions.Policies;
using Microsoft.AspNetCore.Builder;

namespace AuthorizationService.Abstractions.Extensions;

public static class EndpointConventionBuilderExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permissionCode)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(AuthorizationPolicyNames.Permission(permissionCode));
        return builder;
    }
}
