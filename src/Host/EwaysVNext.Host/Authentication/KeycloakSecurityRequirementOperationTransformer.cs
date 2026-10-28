using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace EwaysVNext.Host.Authentication;

public sealed class KeycloakSecurityRequirementOperationTransformer(KeycloakOptions options) : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var requiresAuth = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any();
        if (!requiresAuth)
            return Task.CompletedTask;

        operation.Security = options.Realms.Keys
            .Select(tenant => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(tenant, context.Document)] = [],
            })
            .ToList();

        return Task.CompletedTask;
    }
}
