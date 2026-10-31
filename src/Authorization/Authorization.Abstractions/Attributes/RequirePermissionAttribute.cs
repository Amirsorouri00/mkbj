using System.Reflection;
using System.Security.Claims;
using AuthorizationService.Abstractions.Authorization;
using AuthorizationService.Abstractions.Authorization.Requests;
using EwaysVNext.Core.Application.Security;
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AuthorizationService.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequirePermissionAttribute(string permissionCode) : ObjectFieldDescriptorAttribute
{
    protected override void OnConfigure(IDescriptorContext context, IObjectFieldDescriptor descriptor, MemberInfo? member)
    {
        descriptor.Use(next => async (IMiddlewareContext ctx) =>
        {
            var httpContextAccessor = ctx.Services.GetRequiredService<IHttpContextAccessor>();
            var user = httpContextAccessor.HttpContext?.User;
            var userIdClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("sub")?.Value;

            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                throw new GraphQLException(ErrorBuilder.New()
                    .SetMessage(RequirePermissionAttributeMessages.TokenMissingUserIdentifier)
                    .SetCode("AUTH_NOT_AUTHENTICATED")
                    .Build());
            }

            var checkService = ctx.Services.GetRequiredService<IAuthorizationCheckService>();
            var result = await checkService.CheckAsync(
                new CheckAuthorizationRequest(
                    userId, permissionCode, ScopeId: null, BackofficeRoles.From(user)),
                ctx.RequestAborted);

            if (!result.Allowed)
            {
                throw new GraphQLException(ErrorBuilder.New()
                    .SetMessage(result.Reason)
                    .SetCode("AUTH_NOT_AUTHORIZED")
                    .Build());
            }

            await next(ctx);
        });
    }
}
