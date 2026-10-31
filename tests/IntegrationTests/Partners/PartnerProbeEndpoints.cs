using EwaysVNext.Core.Application;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.IntegrationTests.Partners;

/// <summary>
/// One endpoint per partner scope, gated by that scope's policy and nothing else.
/// <para>
/// A probe rather than a real endpoint, because the authentication and scope machinery has to be
/// provable before any endpoint depends on it — and because testing it through a business
/// endpoint would mean a failure could be either.
/// </para>
/// </summary>
internal sealed class PartnerProbeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        foreach (var scope in PartnerScopes.All)
        {
            app.MapGet($"/test/partner/{Slug(scope)}", (HttpContext context) =>
            {
                PartnerPrincipal.TryFrom(context.User, out var principal);
                return Result.Success(new { partnerId = principal?.PartnerId }).ToHttpResult();
            })
            .RequireAuthorization(AuthPolicies.Partner(scope));
        }
    }

    public static string Slug(string scope) => scope.Replace(':', '-');
}
